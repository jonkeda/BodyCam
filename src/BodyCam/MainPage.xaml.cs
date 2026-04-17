using BodyCam.ViewModels;

namespace BodyCam;

public partial class MainPage : ContentPage
{
	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;

		if (BindingContext is MainViewModel vm)
		{
			vm.Entries.CollectionChanged += (_, e) =>
			{
				if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && vm.Entries.Count > 0)
				{
					TranscriptList.ScrollTo(vm.Entries.Count - 1, position: ScrollToPosition.End, animate: false);
				}
			};
		}
	}
}
