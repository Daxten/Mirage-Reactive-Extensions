using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Mirage;
using Mirage.Collections;
using Mirage.Serialization;
using UnityEngine;
using UnityEngine.Assertions;

namespace MirageReactiveExtensions.Runtime
{
    [Serializable]
    public class SyncVarPredictive<T> : AsyncReactiveProperty<T>, ISyncObject
    {
        public delegate T Predict(T lastSyncedValue, double syncDeltaTime);

        private static EqualityComparer<T> Comparer = EqualityComparer<T>.Default;
        private CancellationTokenSource _ct;

        [NonSerialized] private bool _isReadOnly;
        private NetworkBehaviour _networkBehaviour;
        private int _predictedFrame;
        private T _predictedValue;

        public SyncVarPredictive() : this(default)
        {
        }

        public SyncVarPredictive(T entity) : base(entity)
        {
            _ct = new CancellationTokenSource();
        }

        public Predict Prediction { get; set; }

        public double LastUpdate { get; private set; }
        public T LastSyncedValue => base.Value;

        public new T Value
        {
            get
            {
                if (_isReadOnly)
                {
                    Assert.IsNotNull(Prediction, "Prediction is not set");
                    UpdatePrediction();
                    return _predictedValue;
                }

                return base.Value;
            }
            set
            {
                Assert.IsFalse(_isReadOnly, "SyncVarPredictive can only be modified on the server");

                base.Value = value;
                DidChange();
            }
        }

        public void UpdatePrediction(bool force = false)
        {
            if (force || _predictedFrame != Time.frameCount)
                _predictedValue = Prediction(base.Value, _networkBehaviour.NetworkTime.Time - LastUpdate);
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

            if (Comparer.Equals(obj, base.Value)) return;

            base.Value = obj;
            LastUpdate = _networkBehaviour.NetworkTime.Time;
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
            _predictedFrame = -1;
            _predictedValue = default;
            _networkBehaviour = null;
            if (!_ct.IsCancellationRequested)
            {
                _ct.Cancel();
                _ct.Dispose();
            }
            _ct = new CancellationTokenSource();
            Value = default;
        }

        public void SetNetworkBehaviour(NetworkBehaviour networkBehaviour)
        {
            _networkBehaviour = networkBehaviour;
        }


        public bool IsDirty { get; private set; }
        public event Action OnChange;

        private void DidChange()
        {
            IsDirty = true;
            OnChange?.Invoke();
        }

        public static implicit operator T(SyncVarPredictive<T> d) => d.Value;
    }
}
