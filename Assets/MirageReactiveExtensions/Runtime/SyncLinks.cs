using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mirage;
using Mirage.Collections;
using Mirage.Serialization;
using UnityEngine;

namespace MirageReactiveExtensions.Runtime
{
    [Serializable]
    public class SyncLinks<T> : ISet<T>, ISyncObject where T : NetworkBehaviour
    {
        private readonly HashSet<T> objects;

        private Dictionary<GameObject, CancellationTokenSource> _observerTokens = new();
        private Dictionary<uint, CancellationTokenSource> _eventuallyAdd = new();

        public int Count => objects.Count;
        public bool IsReadOnly { get; private set; }
        void ISyncObject.SetShouldSyncFrom(bool shouldSync) => IsReadOnly = !shouldSync;

        private CancellationTokenSource _onDespawnTokenSource;
        private CancellationTokenSource NewObserverToken => new();

        private NetworkBehaviour _networkBehaviour;
        internal int ChangeCount => _changes.Count;

        /// <summary>
        /// Raised when an element is added to the list.
        /// Receives the new item
        /// </summary>
        public event Action<T> OnAdd;

        /// <summary>
        /// Raised when the set is cleared
        /// </summary>
        public event Action OnClear;

        /// <summary>
        /// Raised when an item is removed from the set
        /// receives the old item
        /// </summary>
        public event Action<T> OnRemove;

        /// <summary>
        /// Raised after the set has been updated
        /// Note that if there are multiple changes
        /// this event is only raised once.
        /// </summary>
        public event Action OnChange;

        private enum Operation : byte
        {
            OP_ADD,
            OP_CLEAR,
            OP_REMOVE
        }

        private struct Change
        {
            public Operation Operation;
            public uint Item;
        }

        private readonly List<Change> _changes = new();

        // how many changes we need to ignore
        // this is needed because when we initialize the list,
        // we might later receive changes that have already been applied
        // so we need to skip them
        private int _changesAhead;

        public SyncLinks()
        {
            objects = new HashSet<T>();
            _onDespawnTokenSource = new CancellationTokenSource();
            _eventuallyAdd = new();
        }

        public void Reset()
        {
            foreach (var token in _observerTokens.Select(e => e.Value).ToArray())
            {
                token.Cancel();
            }

            _observerTokens.Clear();
            IsReadOnly = false;
            _changes.Clear();
            _changesAhead = 0;
            objects.Clear();
        }

        public void SetNetworkBehaviour(NetworkBehaviour networkBehaviour)
        {
            if (_networkBehaviour != null && networkBehaviour != _networkBehaviour)
            {
                Debug.LogError("NetworkBehaviour should never change for SyncLink.");
                return;
            }

            if (_networkBehaviour == null)
            {
                // Initialize Callbacks
                _networkBehaviour = networkBehaviour;
                _networkBehaviour.Identity.OnStopServer.AddListener(OnStopServer);
                _networkBehaviour.Identity.OnStopClient.AddListener(OnStopClient);
            }
        }

        private void OnStopClient()
        {
            if (_networkBehaviour.IsServer) return;
            FullCleanup();
        }

        private void OnStopServer()
        {
            FullCleanup();
        }

        private void FullCleanup()
        {
            _onDespawnTokenSource.Cancel();
            _onDespawnTokenSource = new CancellationTokenSource();
            foreach (var cancellationTokenSource in _observerTokens.Select(e => e.Value).ToArray())
            {
                cancellationTokenSource.Cancel();
            }
        }

        public bool IsDirty => _changes.Count > 0;

        // throw away all the changes
        // this should be called after a successfull sync
        public void Flush() => _changes.Clear();

        private void AddOperation(Operation op) => AddOperation(op, default);

        private void AddOperation(Operation op, T item)
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("SyncSets can only be modified at the server");
            }

            var change = new Change { Operation = op, Item = item ? item.NetId : 0 };

            _changes.Add(change);
            OnChange?.Invoke();
        }

        public void OnSerializeAll(NetworkWriter writer)
        {
            // if init,  write the full list content
            writer.WritePackedUInt32((uint)objects.Count);

            foreach (var obj in objects)
            {
                writer.WritePackedUInt32(obj.NetId);
            }

            // all changes have been applied already
            // thus the client will need to skip all the pending changes
            // or they would be applied again.
            // So we write how many changes are pending
            writer.WritePackedUInt32((uint)_changes.Count);
        }

        public void OnSerializeDelta(NetworkWriter writer)
        {
            // write all the queued up changes
            writer.WritePackedUInt32((uint)_changes.Count);

            for (var i = 0; i < _changes.Count; i++)
            {
                var change = _changes[i];
                writer.WriteByte((byte)change.Operation);

                switch (change.Operation)
                {
                    case Operation.OP_ADD:
                        writer.WritePackedUInt32(change.Item);
                        break;

                    case Operation.OP_CLEAR:
                        break;

                    case Operation.OP_REMOVE:
                        writer.WritePackedUInt32(change.Item);
                        break;
                }
            }
        }

        public void OnDeserializeAll(NetworkReader reader)
        {
            // if init,  write the full list content
            var count = (int)reader.ReadPackedUInt32();
            var locator = reader.ToMirageReader().ObjectLocator;

            objects.Clear();
            _changes.Clear();
            OnClear?.Invoke();

            for (var i = 0; i < count; i++)
            {
                var netId = reader.ReadPackedUInt32();
                EventuallyAdd(locator, netId).Forget();
            }

            // We will need to skip all these changes
            // the next time the list is synchronized
            // because they have already been applied
            _changesAhead = (int)reader.ReadPackedUInt32();
        }

        private async UniTask EventuallyAdd(IObjectLocator locator, uint netId)
        {
            NetworkIdentity networkIdentity = null;
            if (!locator.TryGetIdentity(netId, out networkIdentity))
            {
                if (!_eventuallyAdd.TryGetValue(netId, out var cts))
                {
                    cts = new CancellationTokenSource();
                    _eventuallyAdd[netId] = cts;
                }

                await UniTask.WaitUntil(() => locator.TryGetIdentity(netId, out networkIdentity),
                    cancellationToken: cts.Token, timing: PlayerLoopTiming.EarlyUpdate);
            }

            var comp = networkIdentity.GetComponent<T>();
            objects.Add(comp);
            RemoveOnDestroy(comp).Forget();
            OnAdd?.Invoke(comp);
            OnChange?.Invoke();
        }


        private void Remove(IObjectLocator locator, uint netId)
        {
            NetworkIdentity networkIdentity = null;
            if (!locator.TryGetIdentity(netId, out networkIdentity))
            {
                if (_eventuallyAdd.TryGetValue(netId, out var cts))
                {
                    cts.Cancel();
                    _eventuallyAdd.Remove(netId);
                }
                else
                {
                    Debug.LogWarning($"Could not remove netId: {netId}, this state should be impossible.");
                }
            }
            else
            {
                var comp = networkIdentity.GetComponent<T>();
                if (_observerTokens.TryGetValue(comp.gameObject, out var t))
                {
                    t.Cancel();
                    _observerTokens.Remove(comp.gameObject);
                }

                if (objects.Remove(comp))
                {
                    OnRemove?.Invoke(comp);
                    OnChange?.Invoke();
                }
            }
        }

        public void OnDeserializeDelta(NetworkReader reader)
        {
            var raiseOnChange = false;
            var locator = reader.ToMirageReader().ObjectLocator;

            var changesCount = (int)reader.ReadPackedUInt32();
            uint netId = 0;

            for (var i = 0; i < changesCount; i++)
            {
                var operation = (Operation)reader.ReadByte();


                // apply the operation only if it is a new change
                // that we have not applied yet
                var apply = _changesAhead == 0;
                switch (operation)
                {
                    case Operation.OP_ADD:
                        netId = reader.ReadPackedUInt32();
                        if (apply) EventuallyAdd(locator, netId).Forget();
                        break;
                    case Operation.OP_CLEAR:
                        DeserializeClear(apply);
                        break;
                    case Operation.OP_REMOVE:
                        netId = reader.ReadPackedUInt32();
                        if (apply) Remove(locator, netId);
                        break;
                }

                if (apply)
                {
                    raiseOnChange = true;
                }
                // we just skipped this change
                else
                {
                    _changesAhead--;
                }
            }

            if (raiseOnChange)
            {
                OnChange?.Invoke();
            }
        }

        private void DeserializeClear(bool apply)
        {
            if (apply)
            {
                objects.Clear();
                OnClear?.Invoke();
            }
        }

        public bool Add(T item)
        {
            if (item == null) return false;

            if (objects.Add(item))
            {
                RemoveOnDestroy(item).Forget();
                OnAdd?.Invoke(item);
                AddOperation(Operation.OP_ADD, item);
                return true;
            }

            return false;
        }

        private async UniTaskVoid RemoveOnDestroy(T item)
        {
            CancellationTokenSource ct;
            if (!_observerTokens.TryGetValue(item.gameObject, out ct))
            {
                ct = NewObserverToken;
                _observerTokens[item.gameObject] = ct;
            }

            await item.OnDespawnAsyncWithCancellationToken(ct.Token);
            _observerTokens.Remove(item.gameObject);
            if (objects.Remove(item))
            {
                if (_networkBehaviour.IsServer) AddOperation(Operation.OP_REMOVE, item);
                OnRemove?.Invoke(item);
            }
        }

        void ICollection<T>.Add(T item) => _ = Add(item);

        public void Clear()
        {
            foreach (var token in _observerTokens.Select(e => e.Value).ToArray())
            {
                token.Cancel();
            }

            objects.Clear();
            _observerTokens.Clear();
            OnClear?.Invoke();
            AddOperation(Operation.OP_CLEAR);
        }

        public bool Contains(T item) => objects.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => objects.CopyTo(array, arrayIndex);

        public bool Remove(T item)
        {
            if (!item) return false;

            if (objects.Remove(item))
            {
                AddOperation(Operation.OP_REMOVE, item);
                OnRemove?.Invoke(item);
                return true;
            }

            if (_observerTokens.TryGetValue(item.gameObject, out var token))
            {
                token.Cancel();
            }

            return false;
        }

        public IEnumerator<T> GetEnumerator() => objects.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == this)
            {
                Clear();
                return;
            }

            // remove every element in other from this
            foreach (var element in other)
            {
                Remove(element);
            }
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            if (other is ISet<T> otherSet)
            {
                IntersectWithSet(otherSet);
            }
            else
            {
                var otherAsSet = new HashSet<T>(other);
                IntersectWithSet(otherAsSet);
            }
        }

        private void IntersectWithSet(ISet<T> otherSet)
        {
            var elements = new List<T>(objects);

            foreach (var element in elements)
            {
                if (!otherSet.Contains(element))
                {
                    Remove(element);
                }
            }
        }

        public bool IsProperSubsetOf(IEnumerable<T> other) => objects.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<T> other) => objects.IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<T> other) => objects.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<T> other) => objects.IsSupersetOf(other);

        public bool Overlaps(IEnumerable<T> other) => objects.Overlaps(other);

        public bool SetEquals(IEnumerable<T> other) => objects.SetEquals(other);

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == this)
            {
                Clear();
            }
            else
            {
                foreach (var element in other)
                {
                    if (!Remove(element))
                    {
                        Add(element);
                    }
                }
            }
        }

        public void UnionWith(IEnumerable<T> other)
        {
            if (other != this)
            {
                foreach (var element in other)
                {
                    Add(element);
                }
            }
        }
    }
}
