using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KageDock.Models;
using System.Windows.Input;
using WpfColor = System.Windows.Media.Color;

namespace KageDock.Views
{
    public partial class MyAppsView
    {
         private UIElement BuildAppTile(AppShortcut shortcut)
        {
            var tileBackground = new SolidColorBrush(
                WpfColor.FromArgb(45, 12, 15, 21));

            var tileBorder = new SolidColorBrush(
                WpfColor.FromArgb(35, 220, 230, 245));

            var hoverBackground = WpfColor.FromArgb(65, 220, 230, 245);
            var normalBackground = WpfColor.FromArgb(45, 12, 15, 21);

            var hoverBorder = WpfColor.FromArgb(100, 235, 242, 255);
            var normalBorder = WpfColor.FromArgb(35, 220, 230, 245);

            var tile = new Border
            {
                Width = 76,
                Height = 76,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = tileBorder,
                Background = tileBackground,
                Cursor = System.Windows.Input.Cursors.Hand,
                AllowDrop = true,
                Tag = shortcut,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(1.0, 1.0),

                    // Hover lift and drop ripple.
                    new TranslateTransform(0, 0),

                    // Drag insertion preview movement.
                    new TranslateTransform(0, 0),

                    // Section row reposition animation.
                    new TranslateTransform(0, 0)
                }
            }
            };

            var content = new Grid { Margin = new Thickness(3, 5, 3, 4) };
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            Border? newAppGlow = null;

            if (_newAppHighlightExpirations.TryGetValue(shortcut, out var expiresAt) &&
                expiresAt > DateTime.UtcNow)
            {
                newAppGlow = new Border
                {
                    Margin = new Thickness(-5),
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(
                        WpfColor.FromRgb(190, 225, 255)),
                    Background = System.Windows.Media.Brushes.Transparent,
                    IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = WpfColor.FromRgb(145, 205, 255),
                        BlurRadius = 12,
                        ShadowDepth = 0,
                        Opacity = 0.65
                    }
                };

                Grid.SetRowSpan(newAppGlow, 2);
                content.Children.Add(newAppGlow);

                var remainingTime = expiresAt - DateTime.UtcNow;
                var fadeDelay = remainingTime - TimeSpan.FromMilliseconds(500);

                if (fadeDelay < TimeSpan.Zero)
                    fadeDelay = TimeSpan.Zero;

                newAppGlow.BeginAnimation(
                    UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation
                    {
                        To = 0,
                        BeginTime = fadeDelay,
                        Duration = new Duration(TimeSpan.FromMilliseconds(500))
                    });
            }
            var icon = new System.Windows.Controls.Image
            {
                Source = shortcut.CachedIcon,
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (shortcut.CachedIcon == null &&
                !string.IsNullOrWhiteSpace(shortcut.TargetPath))
            {
                _ = LoadTileIconAsync(shortcut);
            }

            content.Children.Add(icon);

            var label = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(shortcut.DisplayName)
                    ? shortcut.Name
                    : shortcut.DisplayName,
                Foreground = new SolidColorBrush(
                    WpfColor.FromRgb(233, 235, 239)),
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 26,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetRow(label, 1);
            content.Children.Add(label);
            tile.Child = content;


            
            if (newAppGlow != null)
            {
                var reflectionTransform =
                    new System.Windows.Media.TranslateTransform(-1.2, 0);

                var reflectionBrush =
                    new System.Windows.Media.LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 1),
                        RelativeTransform = reflectionTransform,
                        GradientStops = new System.Windows.Media.GradientStopCollection
                        {
                            new(WpfColor.FromArgb(0, 255, 255, 255), 0.0),
                            new(WpfColor.FromArgb(0, 255, 255, 255), 0.40),
                            new(WpfColor.FromArgb(170, 255, 255, 255), 0.50),
                            new(WpfColor.FromArgb(0, 255, 255, 255), 0.60),
                            new(WpfColor.FromArgb(0, 255, 255, 255), 1.0)
                        }
                    };

                var reflection = new Border
                {
                    Margin = new Thickness(-3, -5, -3, -4),
                    CornerRadius = new CornerRadius(5),
                    Background = reflectionBrush,
                    IsHitTestVisible = false,
                    Opacity = 0.65
                };

                Grid.SetRowSpan(reflection, 2);
                System.Windows.Controls.Panel.SetZIndex(reflection, 10);
                content.Children.Add(reflection);

                var sweepAnimation =
                    new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
                    {
                        Duration = new Duration(TimeSpan.FromSeconds(1)),
                        RepeatBehavior =
                            new System.Windows.Media.Animation.RepeatBehavior(3)
                    };

                sweepAnimation.KeyFrames.Add(
                    new System.Windows.Media.Animation.LinearDoubleKeyFrame(
                        -1.2,
                        System.Windows.Media.Animation.KeyTime.FromTimeSpan(
                            TimeSpan.Zero)));

                sweepAnimation.KeyFrames.Add(
                    new System.Windows.Media.Animation.LinearDoubleKeyFrame(
                        1.2,
                        System.Windows.Media.Animation.KeyTime.FromTimeSpan(
                            TimeSpan.FromMilliseconds(700))));

                sweepAnimation.KeyFrames.Add(
                    new System.Windows.Media.Animation.DiscreteDoubleKeyFrame(
                        1.2,
                        System.Windows.Media.Animation.KeyTime.FromTimeSpan(
                            TimeSpan.FromSeconds(1))));

                reflectionTransform.BeginAnimation(
                    System.Windows.Media.TranslateTransform.XProperty,
                    sweepAnimation);
            }



            if (newAppGlow != null &&
                tile.RenderTransform is TransformGroup entranceGroup)
            {
                var scale = entranceGroup.Children
                    .OfType<ScaleTransform>()
                    .FirstOrDefault();

                var translate = entranceGroup.Children
                    .OfType<TranslateTransform>()
                    .FirstOrDefault();

                var easing = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode =
                        System.Windows.Media.Animation.EasingMode.EaseOut
                };

                var duration = TimeSpan.FromMilliseconds(280);

                tile.BeginAnimation(
                    UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = new Duration(duration),
                        EasingFunction = easing
                    });

                if (scale != null)
                {
                    var scaleAnimation =
                        new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.97,
                            To = 1.0,
                            Duration = new Duration(duration),
                            EasingFunction = easing
                        };

                    scale.BeginAnimation(
                        ScaleTransform.ScaleXProperty, scaleAnimation);

                    scale.BeginAnimation(
                        ScaleTransform.ScaleYProperty, scaleAnimation);
                }

                if (translate != null)
                {
                    translate.BeginAnimation(
                        TranslateTransform.YProperty,
                        new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 5,
                            To = 0,
                            Duration = new Duration(duration),
                            EasingFunction = easing
                        });
                }
            }

            void AnimateTileScale(double targetScale, int durationMs)
            {
                if (tile.RenderTransform is not TransformGroup group)
                    return;

                var scale = group.Children
                    .OfType<ScaleTransform>()
                    .FirstOrDefault();

                var translate = group.Children
                    .OfType<TranslateTransform>()
                    .FirstOrDefault();

                if (scale == null || translate == null)
                    return;

                var easing = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode =
                        System.Windows.Media.Animation.EasingMode.EaseOut
                };

                var scaleAnimation =
                    new System.Windows.Media.Animation.DoubleAnimation
                    {
                        To = targetScale,
                        Duration = TimeSpan.FromMilliseconds(durationMs),
                        EasingFunction = easing
                    };

                scale.BeginAnimation(
                    ScaleTransform.ScaleXProperty, scaleAnimation);
                scale.BeginAnimation(
                    ScaleTransform.ScaleYProperty, scaleAnimation);

                var liftAnimation =
                    new System.Windows.Media.Animation.DoubleAnimation
                    {
                        To = targetScale > 1.0 ? -2.0 : 0.0,
                        Duration = TimeSpan.FromMilliseconds(durationMs),
                        EasingFunction = easing
                    };

                translate.BeginAnimation(
                    TranslateTransform.YProperty, liftAnimation);
            }

            tile.MouseEnter += (_, _) =>
            {
                if (_dragStarted)
                    return;

                AnimateTileScale(1.06, 150);

                tileBackground.BeginAnimation(
                    SolidColorBrush.ColorProperty,
                    new System.Windows.Media.Animation.ColorAnimation
                    {
                        To = hoverBackground,
                        Duration = new Duration(
                            TimeSpan.FromMilliseconds(150))
                    });

                tileBorder.BeginAnimation(
                    SolidColorBrush.ColorProperty,
                    new System.Windows.Media.Animation.ColorAnimation
                    {
                        To = hoverBorder,
                        Duration = new Duration(
                            TimeSpan.FromMilliseconds(150))
                    });

                if (tile.RenderTransform is TransformGroup group)
                {
                    var scale = group.Children
                        .OfType<ScaleTransform>()
                        .FirstOrDefault();

                    if (scale != null)
                    {
                        var breathing =
                            new System.Windows.Media.Animation.DoubleAnimation
                            {
                                From = 1.06,
                                To = 1.075,
                                Duration = TimeSpan.FromMilliseconds(900),
                                AutoReverse = true,
                                RepeatBehavior =
                                    System.Windows.Media.Animation.RepeatBehavior.Forever,
                                EasingFunction =
                                    new System.Windows.Media.Animation.SineEase
                                    {
                                        EasingMode =
                                            System.Windows.Media.Animation.EasingMode.EaseInOut
                                    }
                            };

                        scale.BeginAnimation(
                            ScaleTransform.ScaleXProperty, breathing);
                        scale.BeginAnimation(
                            ScaleTransform.ScaleYProperty, breathing);
                    }
                }
            };

            tile.MouseLeave += (_, _) =>
            {
                if (tile.RenderTransform is TransformGroup group)
                {
                    var scale = group.Children
                        .OfType<ScaleTransform>()
                        .FirstOrDefault();

                    if (scale != null)
                    {
                        scale.BeginAnimation(
                            ScaleTransform.ScaleXProperty, null);
                        scale.BeginAnimation(
                            ScaleTransform.ScaleYProperty, null);
                    }
                }

                AnimateTileScale(1.0, 200);

                tileBackground.BeginAnimation(
                    SolidColorBrush.ColorProperty,
                    new System.Windows.Media.Animation.ColorAnimation
                    {
                        To = normalBackground,
                        Duration = new Duration(
                            TimeSpan.FromMilliseconds(200))
                    });

                tileBorder.BeginAnimation(
                    SolidColorBrush.ColorProperty,
                    new System.Windows.Media.Animation.ColorAnimation
                    {
                        To = normalBorder,
                        Duration = new Duration(
                            TimeSpan.FromMilliseconds(200))
                    });
            };

            tile.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragStartPoint = e.GetPosition(this);
                _pendingDragShortcut = shortcut;
                _dragStarted = false;

                AnimateTileScale(0.94, 100);
            };

            tile.MouseMove += async (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                    return;

                if (_pendingDragShortcut != shortcut || _dragStarted)
                    return;

                var current = e.GetPosition(this);

                if (Math.Abs(current.X - _dragStartPoint.X) <
                        SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(current.Y - _dragStartPoint.Y) <
                        SystemParameters.MinimumVerticalDragDistance)
                    return;

                _dragStarted = true;
                _host.SetMyAppsDragActive(true);
                _dragSourceTile = tile;

                if (_newSectionDropZone != null)
                {
                    _newSectionDropZone.Visibility = Visibility.Visible;
                    _newSectionDropZone.Opacity = 1;
                    _newSectionDropZone.IsHitTestVisible = true;
                    StartDropZoneFloatAnimation();

                    _newSectionDropZone.Background =
                        new SolidColorBrush(
                            WpfColor.FromArgb(150, 22, 24, 28));

                    _newSectionDropZone.BorderBrush =
                        new SolidColorBrush(
                            WpfColor.FromArgb(100, 150, 150, 150));
                }

                tile.Opacity = 0.0;

                _host.DragOverlay.Children.Clear();

                var floating = BuildDragGhost(
                    shortcut,
                    tile.ActualWidth,
                    tile.ActualHeight);

                _floatingDragVisual = floating;

                _floatingDragOffset = new System.Windows.Point(
                    tile.ActualWidth / 2,
                    tile.ActualHeight / 2);

                _host.DragOverlay.Children.Add(floating);
                _host.DragOverlay.UpdateLayout();

                var startPosition = Mouse.GetPosition(_host.DragOverlay);

                System.Windows.Controls.Canvas.SetLeft(
                    floating,
                    startPosition.X - _floatingDragOffset.X);

                System.Windows.Controls.Canvas.SetTop(
                    floating,
                    startPosition.Y - _floatingDragOffset.Y);


                try
                {
                    System.Windows.Media.CompositionTarget.Rendering
                        += UpdateFloatingDragVisual;

                    UpdateFloatingDragVisual(null, EventArgs.Empty);

                    var dragResult = System.Windows.DragDrop.DoDragDrop(
                        tile,
                        new System.Windows.DataObject(
                            "KageDockShortcut", shortcut),
                        System.Windows.DragDropEffects.Move);

                    if (_dragSourceTile != null &&
                        _floatingDragVisual != null)
                    {
                        if (dragResult ==
                            System.Windows.DragDropEffects.None)
                        {
                            await AnimateDragGhostBackAsync(_dragSourceTile);
                            _pendingDropRippleShortcut = shortcut;
                        }
                        else if (_pendingDropRippleShortcut == null)
                        {
                            _pendingDropRippleShortcut = shortcut;
                        }
                    }
                }
                finally
                {
                    System.Windows.Media.CompositionTarget.Rendering
                        -= UpdateFloatingDragVisual;

                    _host.DragOverlay.Children.Clear();
                    _floatingDragVisual = null;
                    _pendingDragShortcut = null;

                    _dragAutoScrollTimer?.Stop();
                    _lastDragAutoScrollTick = DateTime.MinValue;
                    _dragStarted = false;
                    _host.SetMyAppsDragActive(false);

                    _previewGrid = null;
                    _previewDraggedShortcut = null;
                    _previewCategory = null;
                    _previewInsertSlot = -1;

                    if (_dragSourceTile != null)
                    {
                        _dragSourceTile.Opacity = 1.0;
                        _dragSourceTile.RenderTransform = Transform.Identity;
                        _dragSourceTile = null;
                    }

                    if (!_sectionsRefreshedDuringDrag)
                        RefreshSections();

                    _sectionsRefreshedDuringDrag = false;

                    // Recheck collapse immediately after drag cleanup.
                     if (_host.IsDockExpanded &&
                        !_dragStarted &&
                        !_host.IsContextMenuOpen &&
                        !_host.IsChildWindowVisible &&
                        System.Windows.Input.Mouse.LeftButton ==
                            System.Windows.Input.MouseButtonState.Released &&
                        !_host.IsDockPanelMouseOver &&
                        !_host.IsTopHoverTriggerMouseOver)
                    {
                        _host.ScheduleCollapse();
                    }

                    if (_pendingDropRippleShortcut != null)
                    {
                        var shortcutToAnimate = _pendingDropRippleShortcut;
                        _pendingDropRippleShortcut = null;

                        await Dispatcher.InvokeAsync(() =>
                        {
                            SectionsPanel.UpdateLayout();
                            AnimateDropRipple(shortcutToAnimate);

                        }, System.Windows.Threading.DispatcherPriority.Render);
                    }

                    if (_newSectionDropZone != null)
                    {
                        _newSectionDropZone.Visibility =
                            System.Windows.Visibility.Collapsed;

                        _newSectionDropZone.Background =
                            new SolidColorBrush(
                                WpfColor.FromArgb(150, 22, 24, 28));

                        _newSectionDropZone.BorderBrush =
                            new SolidColorBrush(
                                WpfColor.FromArgb(100, 150, 150, 150));
                    }

                    // Recheck collapse after the drag operation has fully completed.
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                        () =>
                        {

                            if (!_host.IsDockExpanded ||
                                _dragStarted ||
                                _host.IsContextMenuOpen)
                                return;

                            if (_host.IsChildWindowVisible)
                                return;

                            if (System.Windows.Input.Mouse.LeftButton !=
                                System.Windows.Input.MouseButtonState.Released)
                                return;

                            if (!_host.IsDockPanelMouseOver &&
                                !_host.IsTopHoverTriggerMouseOver)
                            {
                                _host.ScheduleCollapse();
                            }
                        },
                        System.Windows.Threading.DispatcherPriority.Background);

                }
            };

            tile.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (_dragStarted)
                    return;

                if (ReferenceEquals(_pendingDragShortcut, shortcut))
                {
                    AnimateTileScale(1.0, 140);

                    LaunchApp(shortcut);
                    e.Handled = true;
                }

                _pendingDragShortcut = null;

                _dragAutoScrollTimer?.Stop();
                _lastDragAutoScrollTick = DateTime.MinValue;
            };

            tile.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;

                var menu = new System.Windows.Controls.Primitives.Popup
                {
                    PlacementTarget = tile,
                    Placement =
                        System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                    AllowsTransparency = true,
                    StaysOpen = false,
                    PopupAnimation =
                        System.Windows.Controls.Primitives.PopupAnimation.Fade
                };

                var panel = new StackPanel
                {
                    Width = 190,
                    Background = new SolidColorBrush(
                        WpfColor.FromRgb(29, 32, 39))
                };

                var frame = new Border
                {
                    Background = new SolidColorBrush(
                        WpfColor.FromRgb(29, 32, 39)),
                    BorderBrush = new SolidColorBrush(
                        WpfColor.FromRgb(65, 70, 82)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(5),
                    Child = panel,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 18,
                        ShadowDepth = 4,
                        Opacity = 0.35
                    }
                };

                void AddMenuItem(string text, Action action, bool danger = false)
                {
                    var item = new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12, 10, 12, 10),
                        Background = System.Windows.Media.Brushes.Transparent,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    var menuLabel = new TextBlock
                    {
                        Text = text,
                        FontSize = 12,
                        Foreground = danger
                            ? new SolidColorBrush(
                                WpfColor.FromRgb(255, 115, 115))
                            : new SolidColorBrush(
                                WpfColor.FromRgb(235, 237, 242))
                    };

                    item.Child = menuLabel;

                    item.MouseEnter += (_, _) =>
                        item.Background = new SolidColorBrush(
                            WpfColor.FromRgb(48, 53, 63));

                    item.MouseLeave += (_, _) =>
                        item.Background =
                            System.Windows.Media.Brushes.Transparent;

                    item.MouseLeftButtonUp += (_, click) =>
                    {
                        click.Handled = true;
                        menu.IsOpen = false;
                        action();
                    };

                    panel.Children.Add(item);
                }

                AddMenuItem("Rename app", () => RenameApp(shortcut));

                panel.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(5, 3, 5, 3),
                    Background = new SolidColorBrush(
                        WpfColor.FromRgb(55, 59, 68))
                });

                AddMenuItem("Remove from KageDock", () =>
                {
                    var targetTile = FindTileByShortcut(
                        SectionsPanel, shortcut);

                    if (targetTile == null)
                    {
                        _shortcutCoordinator!.RemoveApp(shortcut);
                        SaveAndRefresh();
                        return;
                    }

                    targetTile.IsHitTestVisible = false;

                    var duration = TimeSpan.FromMilliseconds(180);

                    var easing = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode =
                            System.Windows.Media.Animation.EasingMode.EaseIn
                    };

                    var fade =
                        new System.Windows.Media.Animation.DoubleAnimation
                        {
                            To = 0,
                            Duration = new Duration(duration),
                            EasingFunction = easing
                        };

                    fade.Completed += (_, _) =>
                    {
                        _shortcutCoordinator!.RemoveApp(shortcut);
                        SaveAndRefresh();
                    };

                    targetTile.BeginAnimation(
                        UIElement.OpacityProperty, fade);
                }, danger: true);

                menu.Child = frame;

                menu.Opened += (_, _) =>
                {
                    _host.SetContextMenuOpen(true);
                    _host.CancelCollapseTimer();
                };

                menu.Closed += (_, _) =>
                {
                    _host.SetContextMenuOpen(false);

                    if (!_host.IsWindowMouseOver &&
                        System.Windows.Input.Mouse.LeftButton ==
                        System.Windows.Input.MouseButtonState.Released)
                    {
                        _host.ScheduleCollapse();
                    }
                };

                menu.IsOpen = true;
            };

            tile.DragOver += Section_DragOver;
            tile.Drop += Section_Drop;

            return tile;
        }

        private async Task LoadTileIconAsync(AppShortcut shortcut)
        {
            try
            {
                var icon = await _iconService.GetIconAsync(shortcut.TargetPath);

                if (icon == null)
                {
                    Debug.WriteLine(
                        $"Icon extraction returned null: {shortcut.Name} | {shortcut.TargetPath}");
                    return;
                }

                // Always cache the extracted icon, even if the window is unloaded.
                shortcut.CachedIcon = icon;

                if (!IsLoaded)
                    return;

                // Find the current tile instead of updating a potentially
                // detached Image from an older dock refresh.
                var currentTile = FindTileByShortcut(SectionsPanel, shortcut);

                if (currentTile?.Child is not Grid content)
                    return;

                var currentIcon = content.Children
                    .OfType<System.Windows.Controls.Image>()
                    .FirstOrDefault();

                if (currentIcon != null)
                    currentIcon.Source = icon;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Icon loading failed for {shortcut.Name}: {ex.Message}");
            }
        }

         private Border? FindTileByShortcut(
            DependencyObject parent,
            AppShortcut shortcut)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper
                    .GetChild(parent, i);

                if (child is Border border &&
                    ReferenceEquals(border.Tag, shortcut))
                {
                    return border;
                }

                var result = FindTileByShortcut(child, shortcut);

                if (result != null)
                    return result;
            }

            return null;
        }

        private void RenameApp(AppShortcut app)
        {
            var tile = FindTileByShortcut(SectionsPanel, app);

            if (tile == null)
                return;

            var content = tile.Child as Grid;

            if (content == null || content.Children.Count < 2)
                return;

            var label = content.Children
                .OfType<TextBlock>()
                .FirstOrDefault();

            if (label == null)
                return;

            var originalText = label.Text;

           var input = new System.Windows.Controls.TextBox
            {
                Text = originalText,
                FontSize = label.FontSize,
                Foreground = new SolidColorBrush(
                    WpfColor.FromRgb(240, 242, 246)),
                Background = new SolidColorBrush(
                    WpfColor.FromRgb(29, 32, 38)),
                BorderBrush = new SolidColorBrush(
                    WpfColor.FromRgb(75, 80, 89)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                MaxLength = 80,
                CaretBrush = System.Windows.Media.Brushes.White,
                SelectionBrush = new SolidColorBrush(
                    WpfColor.FromArgb(220, 75, 85, 100)),
                SelectionTextBrush = System.Windows.Media.Brushes.White,
                FocusVisualStyle = null
            };

            input.Template = new System.Windows.Controls.ControlTemplate(
                typeof(System.Windows.Controls.TextBox))
            {
                VisualTree = CreateRenameTextBoxTemplate()
            };
            input.Resources[System.Windows.SystemColors.HighlightBrushKey] =
                new SolidColorBrush(WpfColor.FromRgb(100, 108, 120));

            input.Resources[System.Windows.SystemColors.HighlightTextBrushKey] =
                System.Windows.Media.Brushes.White;

            var row = Grid.GetRow(label);
            var column = Grid.GetColumn(label);

            content.Children.Remove(label);
            Grid.SetRow(input, row);
            Grid.SetColumn(input, column);
            content.Children.Add(input);

            input.Focus();
            input.SelectAll();

            bool finished = false;

            void FinishRename(bool save)
            {
                if (finished)
                    return;

                finished = true;

                if (save && !string.IsNullOrWhiteSpace(input.Text))
                {
                    _shortcutCoordinator!.RenameApp(app, input.Text);
                    SaveAndRefresh();
                }
                else
                {
                    // Restore the original label if cancelled.
                    content.Children.Remove(input);
                    content.Children.Add(label);
                }
            }

            input.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    FinishRename(true);
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    e.Handled = true;
                    FinishRename(false);
                }
            };

            input.LostKeyboardFocus += (_, _) =>
            {
                FinishRename(true);
            };
        }

        private static FrameworkElementFactory CreateRenameTextBoxTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(
                    System.Windows.Controls.TextBox.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty,
                new TemplateBindingExtension(
                    System.Windows.Controls.TextBox.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty,
                new TemplateBindingExtension(
                    System.Windows.Controls.TextBox.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var scroll = new FrameworkElementFactory(
                typeof(System.Windows.Controls.ScrollViewer));
            scroll.Name = "PART_ContentHost";
            scroll.SetValue(
                System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty,
                System.Windows.Controls.ScrollBarVisibility.Hidden);
            scroll.SetValue(
                System.Windows.Controls.ScrollViewer.VerticalScrollBarVisibilityProperty,
                System.Windows.Controls.ScrollBarVisibility.Hidden);

            border.AppendChild(scroll);
            return border;
        }

        private void LaunchApp(AppShortcut shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut.TargetPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = shortcut.TargetPath,
                    Arguments = shortcut.Arguments ?? string.Empty,
                    UseShellExecute = true
                });
                _host.CollapseDock();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not launch this app.\n\n{ex.Message}", "KageDock",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}