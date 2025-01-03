using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mirage;
using Mirage.Collections;
using Mirage.Serialization;
using UnityEngine.Assertions;

namespace MirageReactiveExtensions.Runtime
{
    [Serializable]
    public class SyncVar<T> : AsyncReactiveProperty<T>, ISyncObject
    {
        private static EqualityComparer<T> Comparer = EqualityComparer<T>.Default;
        private CancellationTokenSource _ct;
        [NonSerialized] private bool _isReadOnly;

        public SyncVar() : this(default)
        {
        }

        public SyncVar(T entity) : base(entity)
        {
            _ct = new CancellationTokenSource();
        }

        public new T Value
        {
            get => base.Value;
            set
            {
                Assert.IsFalse(_isReadOnly, "SyncVar can only be modified on the server");

                base.Value = value;
                DidChange();
            }
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

            base.Value = obj;
            DidChange();
        }

        public void OnDeserializeDelta(NetworkReader reader)
        {
            OnDeserializeAll(reader);
        }

        public void Reset()
        {
            _isReadOnly = false;
            IsDirty = false;
            Value = default;
            if (!_ct.IsCancellationRequested)
            {
                _ct.Cancel();
                _ct.Dispose();
            }
            _ct = new CancellationTokenSource();
        }

        public void SetNetworkBehaviour(NetworkBehaviour networkBehaviour)
        {
        }


        public bool IsDirty { get; private set; }
        public event Action OnChange;

        private void DidChange()
        {
            IsDirty = true;
            OnChange?.Invoke();
        }

        public static implicit operator T(SyncVar<T> d) => d.Value;
        public static explicit operator SyncVar<T>(T b) => new(b);
    }
}
