using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Triggers;
using Mirage;
using UnityEngine;

namespace MirageReactiveExtensions.Runtime
{
    public static class AsyncNetworkBehaviourExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
        {
            return gameObject.TryGetComponent(out T t) ? t : gameObject.AddComponent<T>();
        }

        public static AsyncDespawnTrigger GetAsyncDespawnTrigger(this NetworkBehaviour component)
        {
            return GetOrAddComponent<AsyncDespawnTrigger>(component.gameObject);
        }
        
        public static UniTask OnDespawnAsync(this NetworkBehaviour component)
        {
            return component.GetAsyncDespawnTrigger().OnDespawnAsync();
        }
    }
    
    [DisallowMultipleComponent]
    public sealed class AsyncDespawnTrigger : MonoBehaviour
    {
        bool called = false;
        CancellationTokenSource cancellationTokenSource;

        private void Awake()
        {
            var identity = GetComponent<NetworkIdentity>();
            if (identity.IsServer)
            {
                identity.OnStopServer.AddListener(OnDespawn);
                identity.OnStartServer.AddListener(OnSpawned);
            }
            else
            {
                identity.OnStopClient.AddListener(OnDespawn);
                identity.OnStartClient.AddListener(OnSpawned);
            }
        }

        public CancellationToken CancellationToken
        {
            get
            {
                if (cancellationTokenSource == null)
                {
                    cancellationTokenSource = new CancellationTokenSource();
                }

                return cancellationTokenSource.Token;
            }
        }

        private void OnDestroy()
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
        }

        void OnSpawned()
        {
            // Object has been despawned and is now getting reused, e.g. object pool
            if (called)
            {
                called = false;
                cancellationTokenSource = new CancellationTokenSource();
            }
        }
        
        void OnDespawn()
        {
            called = true;

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }

        public UniTask OnDespawnAsync()
        {
            if (called) return UniTask.CompletedTask;
            
            var tcs = new UniTaskCompletionSource();
            CancellationToken.RegisterWithoutCaptureExecutionContext(state =>
            {
                var tcs2 = (UniTaskCompletionSource)state;
                tcs2.TrySetResult();
            }, tcs);

            return tcs.Task;
        }
    }
}
