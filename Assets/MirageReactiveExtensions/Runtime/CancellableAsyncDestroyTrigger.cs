using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MirageReactiveExtensions.Runtime
{
    public static class CancellableAsyncTriggerExtensions
    {
        public static T GetOrAddComponent<T>(this GameObject gameObject) where T : Component
        {
            return gameObject.TryGetComponent(out T t) ? t : gameObject.AddComponent<T>();
        }

        public static CancellableAsyncDestroyTrigger GetCancellableAsyncDestroyTrigger(this GameObject gameObject)
        {
            return GetOrAddComponent<CancellableAsyncDestroyTrigger>(gameObject);
        }

        public static CancellableAsyncDestroyTrigger GetCancellableAsyncDestroyTrigger(this Component component)
        {
            return component.gameObject.GetCancellableAsyncDestroyTrigger();
        }
    }

    [DisallowMultipleComponent]
    public sealed class CancellableAsyncDestroyTrigger : MonoBehaviour
    {
        bool awakeCalled = false;
        bool called = false;
        CancellationTokenSource cancellationTokenSource;

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

        void Awake()
        {
            awakeCalled = true;
        }

        void OnDestroy()
        {
            called = true;

            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
        }

        public UniTask OnDestroyAsync(CancellationToken ct = default)
        {
            if (called) return UniTask.CompletedTask;

            var tcs = new UniTaskCompletionSource();
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct, CancellationToken);
            // OnDestroy = Called Cancel.
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
