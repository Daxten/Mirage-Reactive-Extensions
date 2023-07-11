using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
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
        private NetworkBehaviour _networkBehaviour;
        public bool HasValue => Value;

        private CancellationTokenSource NewTokenForCurrentValue => new();
        private bool _hasCallbackRunning;

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

            _tokenForCurrentValue = NewTokenForCurrentValue;
            _hasCallbackRunning = false;
        }

        public new T Value
        {
            get => base.Value;
            set
            {
                StopWaitingForSpawn();
                if (Value != null)
                {
                    ClearCallbacks(Value);
                }

                base.Value = value;

                if (Value != null)
                {
                    SetCallbacks(Value);
                }
                DidChange();
            }
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
                _hasCallbackRunning = true;
            }
            else
            {
                UpdateNetId();
            }
        }

        private void SwitchToNull()
        {
            if (Value != null)
            {
                StopWaitingForSpawn();
                ClearCallbacks(Value);
                base.Value = null;
            }

            _netId = 0;
            IsDirty = _networkBehaviour.Identity.Server != null;
            _tokenForCurrentValue?.Cancel();
            _tokenForCurrentValue = NewTokenForCurrentValue;
            OnChange?.Invoke();
        }

        private void UpdateNetId()
        {
            StopWaitingForSpawn();
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
            EventuallySetValue(reader).Forget();
        }

        private async UniTaskVoid EventuallySetValue(NetworkReader reader)
        {
            var netId = reader.ReadPackedUInt32();
            if (netId == _netId) return;

            // we do not need to reset the token for null, since we don't have start
            // any tasks for it
            if (_netId > 0)
            {
                _tokenForCurrentValue?.Cancel();
                _tokenForCurrentValue = NewTokenForCurrentValue;
            }

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
                SetCallbacks(Value);
            }
            else
            {
                _netId = 0;
                Value = null;
            }

            OnChange?.Invoke();
        }

        private void ClearCallbacks(T target)
        {
            if (target.Identity.Server != null)
            {
                target.Identity.OnStopServer.RemoveListener(SwitchToNull);
            }
            else
            {
                target.Identity.OnStopClient.RemoveListener(SwitchToNull);
            }
        }

        private void SetCallbacks(T target)
        {
            if (target.Identity.Server != null)
            {
                target.Identity.OnStopServer.AddListener(SwitchToNull);
            }
            else
            {
                target.Identity.OnStopClient.AddListener(SwitchToNull);
            }
        }

        public void OnDeserializeDelta(NetworkReader reader)
        {
            OnDeserializeAll(reader);
        }

        public void Reset()
        {
            _tokenForCurrentValue?.Cancel();
            _tokenForCurrentValue = NewTokenForCurrentValue;
            _netId = 0;
            IsDirty = false;
            StopWaitingForSpawn();
        }

        private void StopWaitingForSpawn()
        {
            if (_hasCallbackRunning)
            {
                Value.Identity.OnStartServer.RemoveListener(UpdateNetId);
                _hasCallbackRunning = false;
            }
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
            _tokenForCurrentValue?.Cancel();
            if (base.Value != null)
            {
                ClearCallbacks(Value);
                base.Value = null;
            }

            _netId = 0;
            IsDirty = false;
            _tokenForCurrentValue?.Cancel();
            _tokenForCurrentValue = NewTokenForCurrentValue;
        }

        private void OnStopServer()
        {
            _tokenForCurrentValue?.Cancel();
            if (base.Value != null)
            {
                ClearCallbacks(Value);
                base.Value = null;
            }

            _netId = 0;
            IsDirty = false;
            _tokenForCurrentValue?.Cancel();
            _tokenForCurrentValue = NewTokenForCurrentValue;
        }

        public bool IsDirty { get; private set; }
        public event Action OnChange;

        public static implicit operator T(SyncLink<T> d) => d.Value;
    }
}
