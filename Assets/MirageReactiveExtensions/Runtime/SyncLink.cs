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
    public interface ISyncLink
    {
    }

    [Serializable]
    public class SyncLink<T> : AsyncReactiveProperty<T>, ISyncObject, ISyncLink where T : NetworkBehaviour
    {
        private uint _netId;

        // Token is used for all ongoing tasks, which can be cancelled when the value changes
        private CancellationTokenSource _tokenForCurrentValue;

        // Token is used, so we do not have to wait for _networkBehaviour to be set
        private CancellationTokenSource _onDestroyNetworkBehaviourToken;
        private NetworkBehaviour _networkBehaviour;
        public bool HasValue => Value;

        private CancellationTokenSource NewTokenForCurrentValue =>
            CancellationTokenSource.CreateLinkedTokenSource(_onDestroyNetworkBehaviourToken.Token);

        public SyncLink() : this(default)
        {
            _netId = 0;
        }

        public SyncLink(T entity) : base(entity)
        {
            if (entity != null)
            {
                _netId = entity.NetId;
            }

            _onDestroyNetworkBehaviourToken = new CancellationTokenSource();
            _tokenForCurrentValue = NewTokenForCurrentValue;
        }

        private void CancelRunningTasks()
        {
        }

        private void DidChange()
        {
            if (!HasValue && _netId != 0)
            {
                SwitchToNull();
                return;
            }

            if (!HasValue && _netId == 0) return;
            if (!HasValue || (Value.Identity.NetId == _netId && Value.Identity.IsSpawned)) return;
            if (!Value.Identity.IsSpawned)
            {
                Value.Identity.OnStartServer.AddListener(UpdateNetId);
            }
            else
            {
                UpdateNetId();
            }
        }

        private void SwitchToNull(bool isDirty = true)
        {
            _netId = 0;
            IsDirty = isDirty;
            _tokenForCurrentValue?.Cancel();
            OnChange?.Invoke();
        }

        private void UpdateNetId()
        {
            Value.Identity.OnStartServer.RemoveListener(UpdateNetId);
            _tokenForCurrentValue?.Cancel();
            _tokenForCurrentValue = NewTokenForCurrentValue;
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
            _tokenForCurrentValue?.Cancel();
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
                    await UniTask.WaitUntil(() => locator.TryGetIdentity(_netId, out target),
                        cancellationToken: _tokenForCurrentValue.Token, timing: PlayerLoopTiming.EarlyUpdate);
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
                UniTask.WaitUntilCanceled(_tokenForCurrentValue.Token),
                target.GetCancellableAsyncDestroyTrigger().OnDestroyAsync(_tokenForCurrentValue.Token),
                target.OnDespawnAsync(_tokenForCurrentValue.Token)
            );

            if (target == Value)
                SwitchToNull(false);
        }

        public void OnDeserializeDelta(NetworkReader reader)
        {
            OnDeserializeAll(reader);
        }

        public void Reset()
        {
            _onDestroyNetworkBehaviourToken?.Cancel();
            _tokenForCurrentValue = NewTokenForCurrentValue;
            _netId = 0;
            IsDirty = false;
        }

        public void SetNetworkBehaviour(NetworkBehaviour networkBehaviour)
        {
            _networkBehaviour = networkBehaviour;
            this.ForEachAsync(_ => DidChange(), _onDestroyNetworkBehaviourToken.Token);
            CleanUpOnDestroy().Forget();
        }

        private async UniTaskVoid CleanUpOnDestroy()
        {
            await _networkBehaviour.GetCancellableAsyncDestroyTrigger()
                .OnDestroyAsync(_onDestroyNetworkBehaviourToken.Token);
            _tokenForCurrentValue?.Cancel();
            _onDestroyNetworkBehaviourToken?.Cancel();
        }

        public bool IsDirty { get; private set; }
        public event Action OnChange;

        public static implicit operator T(SyncLink<T> d) => d.Value;
    }
}
