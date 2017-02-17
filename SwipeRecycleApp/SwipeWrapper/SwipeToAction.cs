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
        View GetFront();
        View GetRevealLeft();
        View GetRevealRight();
        M GetItemData();
    }

    public abstract class SwipeViewHolder<M> : RecyclerView.ViewHolder, IViewHolder<M>
    {
        public M data;
        public View front;
        public View revealLeft;
        public View revealRight;

        public SwipeViewHolder(View view) : base(view)
        {
            var viewGroup = (ViewGroup)view;
            front = viewGroup.FindViewWithTag("front");
            revealLeft = viewGroup.FindViewWithTag("reveal-left");
            revealRight = viewGroup.FindViewWithTag("reveal-right");

            var childCount = viewGroup.ChildCount;
            if (front == null)
                if (childCount < 1)
                    throw new RuntimeException("You must provide a view with tag='front'");
                else
                    front = viewGroup.GetChildAt(childCount - 1);

            if (revealLeft != null && revealRight != null) return;
            if (childCount < 2)
                throw new RuntimeException("You must provide at least one reveal view.");
            else
            {
                // set next to last as revealLeft view only if no revealRight was found
                if (revealLeft == null && revealRight == null)
                    revealLeft = viewGroup.GetChildAt(childCount - 2);

                // if there are enough children assume the revealRight
                var i = childCount - 3;
                if (revealRight == null && i > -1)
                    revealRight = viewGroup.GetChildAt(i);
            }
        }

        public View GetFront() => front;

        public View GetRevealLeft() => revealLeft;

        public View GetRevealRight() => revealRight;

        public M GetItemData() => data;
    }

    public class SwipeToAction<M>
    {
        class OnTouchListener<M> : Object, View.IOnTouchListener
        {
            private SwipeToAction<M> sa;

            public OnTouchListener(SwipeToAction<M> swipeToAction)
            {
                sa = swipeToAction;
            }

            public bool OnTouch(View v, MotionEvent e)
            {
                switch (e.Action & MotionEventActions.Mask)
                {
                    case MotionEventActions.Down:
                        {
                            // http://android-developers.blogspot.com/2010/06/making-sense-of-multitouch.html
                            sa._activePointerId = e.GetPointerId(0);

                            // starting point
                            sa._downX = e.GetX(sa._activePointerId);
                            sa._downY = e.GetY(sa._activePointerId);

                            // to check for long click
                            sa._downTime = new Date().Time;

                            // which item are we touching
                            sa.ResolveItem(sa._downX, sa._downY);

                            break;
                        }

                    case MotionEventActions.Up:
                        {
                            sa._upX = e.GetX();
                            sa._upY = e.GetY();
                            sa._upTime = new Date().Time;
                            sa._activePointerId = INVALID_POINTER_ID;

                            sa.ResolveState();
                            break;
                        }

                    case MotionEventActions.PointerUp:
                        {
                            int pointerIndex = (int)(e.Action &
                                MotionEventActions.PointerIndexMask) >> (int)MotionEventActions.PointerIndexShift;
                            int pointerId = e.GetPointerId(pointerIndex);

                            if (pointerId == sa._activePointerId)
                            {
                                int newPointerIndex = pointerIndex == 0 ? 1 : 0;
                                sa._activePointerId = e.GetPointerId(newPointerIndex);
                            }

                            break;
                        }

                    case MotionEventActions.Move:
                        {
                            int pointerIndex = e.FindPointerIndex(sa._activePointerId);
                            if(pointerIndex==INVALID_POINTER_ID) break;
                            float x = e.GetX(pointerIndex);
                            float dx = x - sa._downX;

                            if (!sa.ShouldMove(dx)) break;

                            // current position. moving only over x-axis
                            sa._frontViewLastX = sa._frontViewX + dx + (dx > 0 ? -REVEAL_THRESHOLD : REVEAL_THRESHOLD);
                            sa._frontView.SetX(sa._frontViewLastX);

                            if (sa._frontViewLastX > 0)
                            {
                                sa.revealRight();
                            }
                            else
                            {
                                sa.revealLeft();
                            }

                            break;
                        }

                    case MotionEventActions.Cancel:
                        {
                            sa._activePointerId = INVALID_POINTER_ID;
                            sa.ResolveState();

                            break;
                        }
                }

                return false;
            }
        }

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

        internal readonly HashSet<View> _runningAnimationsOn = new HashSet<View>();
        internal Queue<int> _swipeQueue = new Queue<int>();


        /** Constructor **/

        public SwipeToAction(RecyclerView recyclerView, ISwipeListener<M> swipeListener)
        {
            this._recyclerView = recyclerView;
            this._swipeListener = swipeListener;

            init();
        }


        /** Private methods **/

        private void init()
        {
            _recyclerView.SetOnTouchListener(new OnTouchListener<M>(this));
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
            if (_runningAnimationsOn.Contains(_touchedView))
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
                // swipeRight();
            }
            else if (_frontViewLastX < _frontViewX - _frontViewW / SWIPE_THRESHOLD_WIDTH_RATIO)
            {
                // swipeLeft();
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
                        _swipeListener.OnLongClick(_touchedViewHolder.GetItemData());
                    }
                    else
                    {
                        _swipeListener.OnClick(_touchedViewHolder.GetItemData());
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
            _frontView = viewHolder.GetFront();
            _revealLeftView = viewHolder.GetRevealLeft();
            _revealRightView = viewHolder.GetRevealRight();
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
                return _revealRightView != null && Math.Abs(dx) > REVEAL_THRESHOLD;
            }
            else
            {
                return _revealLeftView != null && Math.Abs(dx) > REVEAL_THRESHOLD;
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
            int next;
            try
            {
                next = _swipeQueue.Dequeue();
            }
            catch (InvalidOperationException e)
            {
                return;
            }
            // workaround in case a swipe call while dragging

            var pos = Math.Abs(next) - 1;
            if (next < 0)
            {
                swipeLeft(pos);
            }
            else
            {
                swipeRight(pos);
            }
        }

        private class BaseAnimatorListener : Object, Animator.IAnimatorListener
        {
            protected SwipeToAction<M> sa;
            protected View animated;

            public BaseAnimatorListener(SwipeToAction<M> sa, View animated)
            {
                this.sa = sa;
                this.animated = animated;
            }

            public virtual void OnAnimationCancel(Animator animation)
            {
                sa._runningAnimationsOn.Add(animated);
            }

            public virtual void OnAnimationEnd(Animator animation)
            {
                sa._runningAnimationsOn.Remove(animated);
            }

            public virtual void OnAnimationRepeat(Animator animation)
            {
                sa._runningAnimationsOn.Remove(animated);
            }

            public virtual void OnAnimationStart(Animator animation)
            {
                sa._runningAnimationsOn.Add(animated);
            }
        }

        private class RightAnimatorListener : BaseAnimatorListener
        {
            public RightAnimatorListener(SwipeToAction<M> sa, View animated) : base(sa, animated)
            {
            }

            public override void OnAnimationEnd(Animator animation)
            {
                sa._runningAnimationsOn.Remove(animated);
                if (sa._swipeListener.SwipeRight(sa._touchedViewHolder.GetItemData()))
                {
                    sa.ResetPosition();
                }
                else
                {
                    sa.CheckQueue();
                }
            }
        }

        private class LeftAnimatorListener : BaseAnimatorListener
        {
            public LeftAnimatorListener(SwipeToAction<M> sa, View animated) : base(sa, animated)
            {
            }

            public override void OnAnimationEnd(Animator animation)
            {
                sa._runningAnimationsOn.Remove(animated);
                if (sa._swipeListener.SwipeLeft(sa._touchedViewHolder.GetItemData()))
                {
                    sa.ResetPosition();
                }
                else
                {
                    sa.CheckQueue();
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

            View animated = _touchedView;
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

            View animated = _touchedView;
            _frontView.Animate()
                    .SetDuration(SWIPE_ANIMATION_DURATION)
                    .SetInterpolator(new AccelerateInterpolator())
                    .SetListener(new LeftAnimatorListener(this, animated));
        }

        private void revealRight()
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

        private void revealLeft()
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

        public void swipeLeft(int position)
        {
            // workaround in case a swipe call while dragging
            if (_downTime != 0)
            {
                _swipeQueue.Take((position + 1) * -1); //use negative to express direction
                return;
            }
            ResolveItem(position);
            revealLeft();
            SwipeLeft();
        }

        public void swipeRight(int position)
        {
            // workaround in case a swipe call while dragging
            if (_downTime != 0)
            {
                _swipeQueue.Enqueue(position + 1);
                return;
            }
            ResolveItem(position);
            revealRight();
            SwipeRight();
        }
    }
}