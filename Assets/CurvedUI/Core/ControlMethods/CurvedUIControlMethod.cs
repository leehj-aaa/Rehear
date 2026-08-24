using System;
using UnityEngine;

namespace CurvedUI.Core.ControlMethods
{
    [Serializable]
    public class CurvedUIControlMethod
    {
        public virtual string scriptingDefineSymbol => string.Empty;
        
        public virtual string[] requiredAssetsNames => Array.Empty<string>();
        
        public virtual void Initialize(bool isPlayMode){ }

        public virtual ControlArgs Process(Hand usedHand, Camera mainEventCamera) => throw new System.NotImplementedException();

        public virtual Transform GetPointerTransform(Hand usedHand) => throw new System.NotImplementedException();

        public virtual Ray GetEventRay(Hand usedHand, Camera eventCam = null) => throw new System.NotImplementedException();

        public class ControlArgs
        {
            public Ray Ray;
            public bool ButtonState;
        }
    }
}
