#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Switch.LowLevel;

namespace UnityEngine.InputSystem.Switch
{
    [InputControlLayout(stateType = typeof(SwitchControllerVirtualInputState), displayName = "Joy-Con (L)")]
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public class SwitchJoyConLHID : SwitchControllerHID
    {
        static SwitchJoyConLHID()
        {
            RegisterControllerLayout<SwitchJoyConLHID>(productId: 0x2006);
        }

        [RuntimeInitializeOnLoadMethod]
        static void Init() { }

        /// <summary>
        /// The last used/added Joy-Con (L) controller.
        /// </summary>
        public static new SwitchJoyConLHID current { get; private set; }

        protected override SpecificControllerTypeEnum LayoutControllerType => SpecificControllerTypeEnum.LeftJoyCon;

        /// <inheritdoc />
        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }

        /// <inheritdoc />
        protected override void OnRemoved()
        {
            base.OnRemoved();
            if (current == this)
                current = null;
        }
    }

    [InputControlLayout(stateType = typeof(SwitchControllerVirtualInputState), displayName = "Joy-Con (R)")]
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public class SwitchJoyConRHID : SwitchControllerHID
    {
        static SwitchJoyConRHID()
        {
            RegisterControllerLayout<SwitchJoyConRHID>(productId: 0x2007);
        }

        [RuntimeInitializeOnLoadMethod]
        static void Init() { }

        /// <summary>
        /// The last used/added Joy-Con (R) controller.
        /// </summary>
        public static new SwitchJoyConRHID current { get; private set; }

        protected override SpecificControllerTypeEnum LayoutControllerType => SpecificControllerTypeEnum.RightJoyCon;

        /// <inheritdoc />
        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }

        /// <inheritdoc />
        protected override void OnRemoved()
        {
            base.OnRemoved();
            if (current == this)
                current = null;
        }
    }

    [InputControlLayout(stateType = typeof(SwitchControllerVirtualInputState), displayName = "Pro Controller")]
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public class SwitchProControllerNewHID : SwitchControllerHID
    {
        static SwitchProControllerNewHID()
        {
            // The product name differs per platform and connection ("Pro Controller" on macOS), and matching
            // on it is what makes this layout win over the built-in SwitchProControllerHID.
            RegisterControllerLayout<SwitchProControllerNewHID>(productId: 0x2009, matchAnyProductName: true);
        }

        [RuntimeInitializeOnLoadMethod]
        static void Init() { }

        /// <summary>
        /// The last used/added Pro Controller.
        /// </summary>
        public static new SwitchProControllerNewHID current { get; private set; }

        protected override SpecificControllerTypeEnum LayoutControllerType => SpecificControllerTypeEnum.ProController;

        /// <inheritdoc />
        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }

        /// <inheritdoc />
        protected override void OnRemoved()
        {
            base.OnRemoved();
            if (current == this)
                current = null;
        }
    }
}
