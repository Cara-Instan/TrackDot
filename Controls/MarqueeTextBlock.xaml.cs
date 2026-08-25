using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace TrackDot.Controls;

/// <summary>
/// A TextBlock wrapper that automatically and smoothly scrolls (marquees)
/// overflowing text horizontally when the text width exceeds the control bounds.
/// </summary>
public partial class MarqueeTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private Storyboard? _storyboard;

    public MarqueeTextBlock()
    {
        InitializeComponent();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock control)
        {
            control.UpdateAnimation();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAnimation();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAnimation();
    }

    private void OnTextBlockSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAnimation();
    }

    private void StopAnimation()
    {
        if (_storyboard != null)
        {
            _storyboard.Stop();
            _storyboard = null;
        }
        TextTranslate.X = 0;
    }

    private void UpdateAnimation()
    {
        StopAnimation();

        double availableWidth = ActualWidth;
        double textWidth = DisplayTextBlock.ActualWidth;

        if (availableWidth <= 0 || textWidth <= 0)
            return;

        double overflow = textWidth - availableWidth;

        // If text overflows by more than 2 pixels, start smooth scrolling animation
        if (overflow > 2)
        {
            double scrollDistance = overflow + 12; // Extra breathing room
            double speed = 30.0; // pixels per second
            double durationSeconds = Math.Max(2.0, scrollDistance / speed);

            var animation = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true
            };

            // Pause at start (1.5s)
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));

            // Smooth scroll to end
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + durationSeconds))));

            // Pause at end (1.5s)
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + durationSeconds + 1.5))));

            Storyboard.SetTarget(animation, TextTranslate);
            Storyboard.SetTargetProperty(animation, new PropertyPath("X"));

            _storyboard = new Storyboard();
            _storyboard.Children.Add(animation);
            _storyboard.Begin();
        }
    }
}

