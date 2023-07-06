using System;
using System.Threading;
using System.Threading.Tasks;
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

        public static CancellationToken GetDespawnCancellationToken(this NetworkBehaviour component)
        {
            return GetOrAddComponent<AsyncDespawnTrigger>(component.gameObject).CancellationToken;
        }

        public static AsyncDespawnTrigger GetAsyncDespawnTrigger(this NetworkBehaviour component)
        {
            return GetOrAddComponent<AsyncDespawnTrigger>(component.gameObject);
        }

        public static UniTask OnDespawnAsyncWithCancellationToken(this NetworkBehaviour component, CancellationToken ct)
        {
            return component.GetAsyncDespawnTrigger().OnDespawnAsyncWithCancellationToken(ct);
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
        CancellationTokenSource despawnTokenSource;

        private void Awake()
        {
            var identity = GetComponent<NetworkIdentity>();
            if (identity.Server.Active)
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
                if (despawnTokenSource == null)
                {
                    despawnTokenSource = new CancellationTokenSource();
                }

                return despawnTokenSource.Token;
            }
        }

        private void OnDestroy()
        {
            if (!called)
            {
                called = true;
                despawnTokenSource?.Cancel();
                despawnTokenSource?.Dispose();
            }
        }

        void OnSpawned()
        {
            // Object has been despawned and is now getting reused, e.g. object pool
            if (called)
            {
                despawnTokenSource?.Dispose();
                despawnTokenSource = null;
                called = false;
            }
        }

        void OnDespawn()
        {
            if (called) return;
            called = true;
            despawnTokenSource?.Cancel();
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

        public UniTask OnDespawnAsyncWithCancellationToken(CancellationToken ct = default)
        {
            if (called) return UniTask.CompletedTask;

            var tcs = new UniTaskCompletionSource();
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, ct);
            linkedTokenSource.Token
                .RegisterWithoutCaptureExecutionContext(state =>
                {
                    var tcs2 = (UniTaskCompletionSource)state;
                    tcs2.TrySetResult();
                    linkedTokenSource.Cancel();
                    linkedTokenSource.Dispose();
                }, tcs);

            return tcs.Task;
        }
    }
}
