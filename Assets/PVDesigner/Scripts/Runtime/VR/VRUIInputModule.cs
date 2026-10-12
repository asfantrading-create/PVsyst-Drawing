using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PVDesigner.VR
{
    /// <summary>
    /// uGUI input module driven by a laser pointer. A disabled camera placed on the controller
    /// acts as the event camera, the pointer is always at the centre of its viewport.
    /// </summary>
    public class VRUIInputModule : BaseInputModule
    {
        public Camera PointerCamera;
        public Func<bool> IsPressed;
        public Func<Vector2> ScrollDelta;

        public RaycastResult CurrentRaycast { get; private set; }
        public bool IsOverUi => CurrentRaycast.gameObject != null;

        private PointerEventData _data;
        private bool _wasPressed;

        public override void Process()
        {
            if (PointerCamera == null) return;
            _data ??= new PointerEventData(eventSystem) { pointerId = -10 };

            var center = new Vector2(PointerCamera.pixelWidth * 0.5f, PointerCamera.pixelHeight * 0.5f);
            _data.Reset();
            _data.delta = center - _data.position;
            _data.position = center;
            _data.scrollDelta = ScrollDelta != null ? ScrollDelta() : Vector2.zero;
            _data.button = PointerEventData.InputButton.Left;

            eventSystem.RaycastAll(_data, m_RaycastResultCache);
            var hit = FindFirstRaycast(m_RaycastResultCache);
            m_RaycastResultCache.Clear();
            _data.pointerCurrentRaycast = hit;
            CurrentRaycast = hit;

            HandlePointerExitAndEnter(_data, hit.gameObject);

            if (_data.scrollDelta.sqrMagnitude > 0.01f && hit.gameObject != null)
            {
                var scrollHandler = ExecuteEvents.GetEventHandler<IScrollHandler>(hit.gameObject);
                ExecuteEvents.ExecuteHierarchy(scrollHandler, _data, ExecuteEvents.scrollHandler);
            }

            bool pressed = IsPressed != null && IsPressed();
            if (pressed && !_wasPressed) Press(hit);
            else if (!pressed && _wasPressed) Release(hit);
            else if (pressed) Drag();
            _wasPressed = pressed;
        }

        private void Press(RaycastResult hit)
        {
            _data.pressPosition = _data.position;
            _data.pointerPressRaycast = hit;
            _data.eligibleForClick = true;
            _data.useDragThreshold = true;
            _data.dragging = false;

            var go = hit.gameObject;
            if (go == null) return;
            // Clicking somewhere else ends text editing / selection.
            var selectHandler = ExecuteEvents.GetEventHandler<ISelectHandler>(go);
            if (selectHandler != eventSystem.currentSelectedGameObject) eventSystem.SetSelectedGameObject(null, _data);
            var press = ExecuteEvents.ExecuteHierarchy(go, _data, ExecuteEvents.pointerDownHandler);
            if (press == null) press = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            _data.pointerPress = press;
            _data.rawPointerPress = go;
            _data.clickTime = Time.unscaledTime;
            _data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(go);
            if (_data.pointerDrag != null) ExecuteEvents.Execute(_data.pointerDrag, _data, ExecuteEvents.initializePotentialDrag);
        }

        private void Drag()
        {
            if (_data.pointerDrag == null) return;
            if (!_data.dragging)
            {
                ExecuteEvents.Execute(_data.pointerDrag, _data, ExecuteEvents.beginDragHandler);
                _data.dragging = true;
            }
            ExecuteEvents.Execute(_data.pointerDrag, _data, ExecuteEvents.dragHandler);
        }

        private void Release(RaycastResult hit)
        {
            if (_data.pointerPress != null) ExecuteEvents.Execute(_data.pointerPress, _data, ExecuteEvents.pointerUpHandler);
            var clickHandler = hit.gameObject != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject) : null;
            if (_data.pointerPress != null && _data.pointerPress == clickHandler && _data.eligibleForClick)
                ExecuteEvents.Execute(_data.pointerPress, _data, ExecuteEvents.pointerClickHandler);
            if (_data.pointerDrag != null && _data.dragging)
                ExecuteEvents.Execute(_data.pointerDrag, _data, ExecuteEvents.endDragHandler);
            _data.eligibleForClick = false;
            _data.pointerPress = null;
            _data.rawPointerPress = null;
            _data.dragging = false;
            _data.pointerDrag = null;
        }

        public override void DeactivateModule()
        {
            base.DeactivateModule();
            if (_data != null) HandlePointerExitAndEnter(_data, null);
            _wasPressed = false;
        }
    }
}
