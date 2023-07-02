using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using Cysharp.Threading.Tasks.Triggers;
using Mirage;
using Mirage.Collections;
using Mirage.Serialization;
using UnityEngine;

namespace MirageReactiveExtensions.Runtime
{
    public interface ISyncLink { }
    
    [Serializable]
    public class SyncLink<T> : AsyncReactiveProperty<T>, ISyncObject, ISyncLink where T : NetworkBehaviour
    {
        private uint _netId;
        private CancellationTokenSource _ct;
        private CancellationTokenSource _onDestroyToken;
        private static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;
        private NetworkBehaviour _networkBehaviour;
        public bool HasValue => !Comparer.Equals(Value, null);

        public SyncLink() : this(default)
        {
            _netId = 0;
        }

        public SyncLink(T entity) : base(entity)
        {
            if (!Comparer.Equals(entity, null))
            {
                _netId = entity.NetId;
            }

            _ct = new CancellationTokenSource();
            _onDestroyToken = new CancellationTokenSource();
        }

        private void DidChange()
        {
            if (Comparer.Equals(Value, null) && _netId != 0)
            {
                _netId = 0;
                IsDirty = true;
                OnChange?.Invoke();
                return;
            }

            if (Comparer.Equals(Value, null) || (Value.Identity.NetId == _netId && Value.Identity.IsSpawned)) return;

            if (!Value.Identity.IsSpawned)
                Value.Identity.OnStartServer.AddListener(UpdateNetId);
            else
                UpdateNetId();
        }

        private void UpdateNetId()
        {
            _netId = Value.NetId;
            IsDirty = true;
            OnChange?.Invoke();
        }

        void ISyncObject.SetShouldSyncFrom(bool shouldSync)
        {
        }

        public void Flush()
        {
            IsDirty = false;
        }

        public void OnSerializeAll(NetworkWriter writer)
        {
            writer.WritePackedUInt32(_netId);
        }

        public void OnSerializeDelta(NetworkWriter writer)
        {
            OnSerializeAll(writer);
        }

        public void OnDeserializeAll(NetworkReader reader)
        {
            _ct?.Cancel();
            EventuallySetValue(reader).Forget();
        }

        private async UniTaskVoid EventuallySetValue(NetworkReader reader)
        {
            var netId = reader.ReadPackedUInt32();

            if (netId == _netId) return;
            
            if (netId > 0)
            {
                _netId = netId;
                NetworkIdentity target = null;
                var locator = reader.ToMirageReader().ObjectLocator;
                if (!locator.TryGetIdentity(_netId, out target))
                {
                    _ct = CancellationTokenSource.CreateLinkedTokenSource(_networkBehaviour.destroyCancellationToken);
                    await UniTask.WaitUntil(() => locator.TryGetIdentity(_netId, out target),
                        cancellationToken: _ct.Token, timing: PlayerLoopTiming.EarlyUpdate);
                }
                Value = target.GetComponent<T>();
                SetNullOnDestroy(Value).Forget();
            }
            else
            {
                _netId = 0;
                Value = null;
            }

            OnChange?.Invoke();
        }

        private async UniTaskVoid SetNullOnDestroy(T target)
        {
            await UniTask.WhenAny(
                UniTask.WaitUntilCanceled(_ct.Token),
                UniTask.WaitUntilCanceled(_networkBehaviour.destroyCancellationToken),
                target.OnDestroyAsync()
            );

            if (target == Value)
            {
                Value = null;
                OnChange?.Invoke();
            }
        }

        public void OnDeserializeDelta(NetworkReader reader)
        {
            OnDeserializeAll(reader);
        }

        public void Reset()
        {
            _netId = 0;
            IsDirty = false;
        }

        public void SetNetworkBehaviour(NetworkBehaviour networkBehaviour)
        {
            _networkBehaviour = networkBehaviour;
            this.ForEachAsync(_ => DidChange(), _onDestroyToken.Token);
            CleanUpOnDestroy().Forget();
        }

        private async UniTaskVoid CleanUpOnDestroy()
        {
            await UniTask.WaitUntil(() => _networkBehaviour != null);
            await _networkBehaviour.OnDestroyAsync();
            _onDestroyToken.Cancel();
        }

        public bool IsDirty { get; private set; }
        public event Action OnChange;

        public static implicit operator T(SyncLink<T> d) => d.Value;
    }
}