using Xunit;

// WPF refuses to create more than one System.Windows.Application
// per AppDomain. Running WPF-touching tests in parallel leads to
// race conditions where AboutWindowTests constructs a barebones
// Application first and MainWindowShowPopoverTests then cannot
// install App.xaml's resources (which MainWindow.xaml references).
// Disabling assembly-level parallelization serializes all test
// classes, and the [Collection("WPF")] attribute further groups
// the WPF-dependent ones so they run on a single thread.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
