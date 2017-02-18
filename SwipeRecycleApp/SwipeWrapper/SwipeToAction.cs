using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Android.Animation;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Views.Animations;
using Android.Widget;
using Java.Lang;
using Java.Util;
using Math = System.Math;
using static Android.Support.V7.Widget.RecyclerView;
using SwipeRecycleApp.SwipeWrapper;
using Object = Java.Lang.Object;

namespace SwipeRecycleApp.SwipeWrapper
{
    public interface ISwipeListener<M>
    {
        bool SwipeLeft(M itemData);
        bool SwipeRight(M itemData);
        void OnClick(M itemData);
        void OnLongClick(M itemData);
    }

    public interface IViewHolder<M>
    {
        View Front { get; }
        View RevealLeft { get; }
        View RevealRight { get; }
        M ItemData { get; }
    }

    public abstract class SwipeViewHolder<M> : RecyclerView.ViewHolder, IViewHolder<M>
    {
        public SwipeViewHolder(View view) : base(view)
        {
            var viewGroup = (ViewGroup)view;
            Front = viewGroup.FindViewWithTag("front");
            RevealLeft = viewGroup.FindViewWithTag("reveal-left");
            RevealRight = viewGroup.FindViewWithTag("reveal-right");

            var childCount = viewGroup.ChildCount;
            if (Front == null)
                if (childCount < 1)
                    throw new RuntimeException("You must provide a view with tag='front'");
                else
                    Front = viewGroup.GetChildAt(childCount - 1);

            if (RevealLeft != null && RevealRight != null) return;
            if (childCount < 2)
                throw new RuntimeException("You must provide at least one reveal view.");
            else
            {
                // set next to last as revealLeft view only if no revealRight was found
                if (RevealLeft == null && RevealRight == null)
                    RevealLeft = viewGroup.GetChildAt(childCount - 2);

                // if there are enough children assume the revealRight
                var i = childCount - 3;
                if (RevealRight == null && i > -1)
                    RevealRight = viewGroup.GetChildAt(i);
            }
        }

        public View Front { get; }

        public View RevealLeft { get; }

        public View RevealRight { get; }

        public M ItemData { get; }
    }

    public class SwipeToAction<M> : Object, View.IOnTouchListener
    {

        private const int SWIPE_ANIMATION_DURATION = 300;
        private const int RESET_ANIMATION_DURATION = 500;
        private const int REVEAL_THRESHOLD = 50;
        private const int SWIPE_THRESHOLD_WIDTH_RATIO = 5;
        private const int LONG_PRESS_TIME = 500; // 500 is the standard long press time
        private const int INVALID_POINTER_ID = -1;

        public int _activePointerId = INVALID_POINTER_ID;

        internal RecyclerView _recyclerView;
        internal ISwipeListener<M> _swipeListener;
        internal View _touchedView;
        internal SwipeViewHolder<M> _touchedViewHolder;
        internal View _frontView;
        internal View _revealLeftView;
        internal View _revealRightView;

        internal float _frontViewX;
        internal float _frontViewW;
        internal float _frontViewLastX;

        private float _downY;
        private float _downX;
        internal float _upX;
        internal float _upY;

        internal long _downTime;
        internal long _upTime;

        internal readonly HashSet<View> RunningAnimationsOn = new HashSet<View>();
        internal Queue<int> SwipeQueue = new Queue<int>();


        /** Constructor **/

        public SwipeToAction(RecyclerView recyclerView, ISwipeListener<M> swipeListener)
        {
            this._recyclerView = recyclerView;
            this._swipeListener = swipeListener;

            init();
        }


        /** Private methods **/
        public bool OnTouch(View v, MotionEvent e)
        {
            switch (e.Action & MotionEventActions.Mask)
            {
                case MotionEventActions.Down:
                    {
                        // http://android-developers.blogspot.com/2010/06/making-sense-of-multitouch.html
                        _activePointerId = e.GetPointerId(0);

                        // starting point
                        _downX = e.GetX(_activePointerId);
                        _downY = e.GetY(_activePointerId);

                        // to check for long click
                        _downTime = new Date().Time;

                        // which item are we touching
                        ResolveItem(_downX, _downY);

                        break;
                    }

                case MotionEventActions.Up:
                    {
                        _upX = e.GetX();
                        _upY = e.GetY();
                        _upTime = new Date().Time;
                        _activePointerId = INVALID_POINTER_ID;

                        ResolveState();
                        break;
                    }

                case MotionEventActions.PointerUp:
                    {
                        int pointerIndex = (int)(e.Action &
                            MotionEventActions.PointerIndexMask) >> (int)MotionEventActions.PointerIndexShift;
                        int pointerId = e.GetPointerId(pointerIndex);

                        if (pointerId == _activePointerId)
                        {
                            int newPointerIndex = pointerIndex == 0 ? 1 : 0;
                            _activePointerId = e.GetPointerId(newPointerIndex);
                        }

                        break;
                    }

                case MotionEventActions.Move:
                    {
                        int pointerIndex = e.FindPointerIndex(_activePointerId);
                        if (pointerIndex == INVALID_POINTER_ID) break;
                        float x = e.GetX(pointerIndex);
                        float dx = x - _downX;

                        if (!ShouldMove(dx)) break;

                        // current position. moving only over x-axis
                        _frontViewLastX = _frontViewX + dx + (dx > 0 ? - REVEAL_THRESHOLD : REVEAL_THRESHOLD);
                        _frontView.SetX(_frontViewLastX);

                        if (_frontViewLastX > 0)
                        {
                            RevealRight();
                        }
                        else
                        {
                            RevealLeft();
                        }

                        break;
                    }

                case MotionEventActions.Cancel:
                    {
                        _activePointerId = INVALID_POINTER_ID;
                        ResolveState();

                        break;
                    }
            }

            return false;
        }
        private void init()
        {
            _recyclerView.SetOnTouchListener(this);
        }

        internal void ResolveItem(float x, float y)
        {
            _touchedView = _recyclerView.FindChildViewUnder(x, y);
            if (_touchedView == null)
            {
                //no child under
                _frontView = null;
                return;
            }

            // check if the view is being animated. in that case do not allow to move it
            if (RunningAnimationsOn.Contains(_touchedView))
            {
                _frontView = null;
                return;
            }

            InitViewForItem((SwipeViewHolder<M>)_recyclerView.GetChildViewHolder(_touchedView));
        }

        private void ResolveState()
        {
            if (_frontView == null)
            {
                return;
            }

            if (_frontViewLastX > _frontViewX + _frontViewW / SWIPE_THRESHOLD_WIDTH_RATIO)
            {
                SwipeRight();
            }
            else if (_frontViewLastX < _frontViewX - _frontViewW / SWIPE_THRESHOLD_WIDTH_RATIO)
            {
                SwipeLeft();
            }
            else
            {
                float diffX = Math.Abs(_downX - _upX);
                float diffY = Math.Abs(_downY - _upY);

                if (diffX <= 5 && diffY <= 5)
                {
                    int pressTime = (int)(_upTime - _downTime);
                    if (pressTime > LONG_PRESS_TIME)
                    {
                        _swipeListener.OnLongClick(_touchedViewHolder.ItemData);
                    }
                    else
                    {
                        _swipeListener.OnClick(_touchedViewHolder.ItemData);
                    }
                }

                ResetPosition();
            }

            Clear();
        }
        private void ResolveItem(int adapterPosition)
        {
            InitViewForItem((SwipeViewHolder<M>)_recyclerView.FindViewHolderForAdapterPosition(adapterPosition));
        }

        private void InitViewForItem(SwipeViewHolder<M> viewHolder)
        {
            _touchedViewHolder = viewHolder;
            _frontView = viewHolder.Front;
            _revealLeftView = viewHolder.RevealLeft;
            _revealRightView = viewHolder.RevealRight;
            _frontViewX = _frontView.GetX();
            _frontViewW = _frontView.Width;
        }

        private bool ShouldMove(float dx)
        {
            if (_frontView == null)
            {
                return false;
            }

            if (dx > 0)
            {
                var b = _revealRightView != null && Math.Abs(dx) > REVEAL_THRESHOLD;
                return b;
            }
            else
            {
                var b = _revealLeftView != null && Math.Abs(dx) > REVEAL_THRESHOLD;
                return b;
            }
        }

        private void Clear()
        {
            _frontViewX = 0;
            _frontViewW = 0;
            _frontViewLastX = 0;
            _downX = 0;
            _downY = 0;
            _upX = 0;
            _upY = 0;
            _downTime = 0;
            _upTime = 0;
        }

        private void CheckQueue()
        {
            if (SwipeQueue.Count == 0) return;

            var next = SwipeQueue.Dequeue();
            // workaround in case a swipe call while dragging

            var pos = Math.Abs(next) - 1;
            if (next < 0)
            {
                SwipeLeft(pos);
            }
            else
            {
                SwipeRight(pos);
            }
        }

        private class BaseAnimatorListener : Object, Animator.IAnimatorListener
        {
            protected readonly SwipeToAction<M> Sa;
            protected readonly View Animated;

            public BaseAnimatorListener(SwipeToAction<M> sa, View animated)
            {
                this.Sa = sa;
                this.Animated = animated;
            }

            public void OnAnimationCancel(Animator animation)
            {
                Sa.RunningAnimationsOn.Add(Animated);
            }

            public void OnAnimationEnd(Animator animation)
            {
                Sa.RunningAnimationsOn.Remove(Animated);
            }

            public void OnAnimationRepeat(Animator animation)
            {
                Sa.RunningAnimationsOn.Remove(Animated);
            }

            public void OnAnimationStart(Animator animation)
            {
                Sa.RunningAnimationsOn.Add(Animated);
            }
        }

        private class RightAnimatorListener : Object, Animator.IAnimatorListener
        {
            public RightAnimatorListener(SwipeToAction<M> sa, View animated)
            {
            }

            protected readonly SwipeToAction<M> Sa;
            protected readonly View Animated;


            public void OnAnimationCancel(Animator animation)
            {
                Sa.RunningAnimationsOn.Add(Animated);
            }


            public void OnAnimationRepeat(Animator animation)
            {
                Sa.RunningAnimationsOn.Remove(Animated);
            }

            public void OnAnimationStart(Animator animation)
            {
                Sa.RunningAnimationsOn.Add(Animated);
            }

            public void OnAnimationEnd(Animator animation)
            {
                Sa.RunningAnimationsOn.Remove(Animated);
                if (Sa._swipeListener.SwipeRight(Sa._touchedViewHolder.ItemData))
                {
                    Sa.ResetPosition();
                }
                else
                {
                    Sa.CheckQueue();
                }
            }
        }

        private class LeftAnimatorListener : Object, Animator.IAnimatorListener
        {
            protected readonly SwipeToAction<M> Sa;
            protected readonly View Animated;


            public void OnAnimationCancel(Animator animation)
            {
                Sa.RunningAnimationsOn.Add(Animated);
            }


            public void OnAnimationRepeat(Animator animation)
            {
                Sa.RunningAnimationsOn.Remove(Animated);
            }

            public void OnAnimationStart(Animator animation)
            {
                Sa.RunningAnimationsOn.Add(Animated);
            }
            public LeftAnimatorListener(SwipeToAction<M> sa, View animated)
            {
            }

            public void OnAnimationEnd(Animator animation)
            {
                Sa.RunningAnimationsOn.Remove(Animated);
                if (Sa._swipeListener.SwipeLeft(Sa._touchedViewHolder.ItemData))
                {
                    Sa.ResetPosition();
                }
                else
                {
                    Sa.CheckQueue();
                }
            }
        }

        private void ResetPosition()
        {
            if (_frontView == null)
            {
                return;
            }

            var animated = _touchedView;
            _frontView.Animate()
                    .SetDuration(RESET_ANIMATION_DURATION)
                    .SetInterpolator(new AccelerateDecelerateInterpolator())
                    .SetListener(new BaseAnimatorListener(this, animated))
                .X(_frontViewX);
        }

        private void SwipeRight()
        {
            if (_frontView == null)
            {
                return;
            }

            var animated = _touchedView;
            _frontView.Animate()
                    .SetDuration(SWIPE_ANIMATION_DURATION)
                    .SetInterpolator(new AccelerateInterpolator())
                    .SetListener(new RightAnimatorListener(this, animated));
        }

        private void SwipeLeft()
        {
            if (_frontView == null)
            {
                return;
            }

            var animated = _touchedView;
            _frontView.Animate()
                    .SetDuration(SWIPE_ANIMATION_DURATION)
                    .SetInterpolator(new AccelerateInterpolator())
                    .SetListener(new LeftAnimatorListener(this, animated));
        }

        private void RevealRight()
        {
            if (_revealLeftView != null)
            {
                _revealLeftView.Visibility = ViewStates.Gone;
            }

            if (_revealRightView != null)
            {
                _revealRightView.Visibility = ViewStates.Visible;
            }
        }

        private void RevealLeft()
        {
            if (_revealRightView != null)
            {
                _revealRightView.Visibility = ViewStates.Gone;
            }

            if (_revealLeftView != null)
            {
                _revealLeftView.Visibility = ViewStates.Visible;
            }
        }


        /** Exposed methods **/

        public void SwipeLeft(int position)
        {
            // workaround in case a swipe call while dragging
            if (_downTime != 0)
            {
                SwipeQueue.Enqueue((position + 1) * -1); //use negative to express direction
                return;
            }
            ResolveItem(position);
            RevealLeft();
            SwipeLeft();
        }

        public void SwipeRight(int position)
        {
            // workaround in case a swipe call while dragging
            if (_downTime != 0)
            {
                SwipeQueue.Enqueue(position + 1);
                return;
            }
            ResolveItem(position);
            RevealRight();
            SwipeRight();
        }
    }
}