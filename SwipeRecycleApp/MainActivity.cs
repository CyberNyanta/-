using Android.App;
using Android.Widget;
using Android.OS;
using Android.Support.V7.App;
using Android.Support.V7.Widget;
using Android.Views;
using SwipeRecycleApp.SwipeWrapper;

namespace SwipeRecycleApp
{
    [Activity(Label = "SwipeRecycleApp", MainLauncher = true, Icon = "@drawable/icon", Theme = "@style/Theme.AppCompat")]
    public class MainActivity : AppCompatActivity
    {
        private RecyclerView recyclerView;
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            SetContentView(Resource.Layout.activity_airline_review_photos_list);
            recyclerView = FindViewById<RecyclerView>(Resource.Id.list_photos);
            LinearLayoutManager layoutManager = new LinearLayoutManager(this);
            recyclerView.SetLayoutManager(layoutManager);
            recyclerView.SetAdapter(new Adapter());
            var swipeToAction = new SwipeToAction<object>(recyclerView, new Listener());
            // Set our view from the "main" layout resource
            // SetContentView (Resource.Layout.Main);
        }
    }

    class Listener : ISwipeListener<object>
    {
        public bool SwipeLeft(object itemData)
        {
            return true;
        }

        public bool SwipeRight(object itemData)
        {
            return true;
        }

        public void OnClick(object itemData)
        {
            
        }

        public void OnLongClick(object itemData)
        {
            
        }
    }
    class Adapter : RecyclerView.Adapter
    {
        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {        
        }

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            return new MyHolder(LayoutInflater.From(parent.Context).Inflate(Resource.Layout.view_photo_item, parent, false));
        }

        public override int ItemCount => 10;
    }

    class MyHolder : SwipeViewHolder<object>
    {
        public MyHolder(View view) : base(view)
        {
        }
    }
}

