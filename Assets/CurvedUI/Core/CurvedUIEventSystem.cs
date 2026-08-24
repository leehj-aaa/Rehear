using UnityEngine.EventSystems;

namespace CurvedUI.Core
{
    /// <summary>
    /// Fixes the issue where UI input would stop working in VR if the game window looses focus.
    /// </summary>
    public class CurvedUIEventSystem : EventSystem
    {
        protected override void Update()
        {
            if (current != this)
            {
                //If another Event System is being used, we can temporarily switch to this one to allow for both to coexist.
                var originalCurrent = current;
                current = this;
                base.Update();
                current = originalCurrent;
            }
            else
            {
                base.Update();
            }
        }

#if !ENABLE_INPUT_SYSTEM
        protected override void OnApplicationFocus(bool hasFocus)
        {
            base.OnApplicationFocus(true);
        }
#endif
    }
}





