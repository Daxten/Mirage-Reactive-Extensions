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
    [Serializable]
    public class SyncVar<T> : AsyncReactiveProperty<T>, ISyncObject
    {
        [NonSerialized] private bool _isReadOnly;
        private static EqualityComparer<T> Comparer = EqualityComparer<T>.Default;
        private NetworkBehaviour _networkBehaviour;
        private CancellationTokenSource _ct;

        public SyncVar() : this(default)
        {
        }

        public SyncVar(T entity) : base(entity)
        {
            _ct = new CancellationTokenSource();
        }

        private void DidChange()
        {
            IsDirty = true;
            OnChange?.Invoke();
        }

        void ISyncObject.SetShouldSyncFrom(bool shouldSync)
        {
            _isReadOnly = !shouldSync;
        }

        public void Flush()
        {
            IsDirty = false;
        }

        public void OnSerializeAll(NetworkWriter writer)
        {
            writer.Write(Value);
        }

        public void OnSerializeDelta(NetworkWriter writer)
        {
            OnSerializeAll(writer);
        }

        public void OnDeserializeAll(NetworkReader reader)
        {
            var obj = reader.Read<T>();

            if (Comparer.Equals(obj, Value)) return;

            Value = obj;
        }

        public void OnDeserializeDelta(NetworkReader reader)
        {
            OnDeserializeAll(reader);
        }

        public void Reset()
        {
            _ct?.Cancel();
            Value = default;
            _isReadOnly = false;
            IsDirty = false;
        }

        public void SetNetworkBehaviour(NetworkBehaviour networkBehaviour)
        {
            _networkBehaviour = networkBehaviour;
            this.ForEachAsync(_ => DidChange(), _networkBehaviour.destroyCancellationToken);
            CleanUpOnDestroy().Forget();
        }

        private async UniTaskVoid CleanUpOnDestroy()
        {
            await _networkBehaviour.OnDestroyAsync();
            _ct.Cancel();
        }
        

        public bool IsDirty { get; private set; }
        public event Action OnChange;

        public static implicit operator T(SyncVar<T> d) => d.Value;
        public static explicit operator SyncVar<T>(T b) => new(b);
    }
}