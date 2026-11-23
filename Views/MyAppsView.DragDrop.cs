using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using System.IO;
using System.Collections.Generic;
using KageDock.Coordinators;
using KageDock.Models;

namespace KageDock.Views
{
    public partial class MyAppsView
    {
      private void HideDraggedShortcutTile()
    {
        if (!_dragStarted || _pendingDragShortcut == null)
            return;

        var tile = FindTileByShortcut(
                SectionsPanel,
                _pendingDragShortcut);

        if (tile != null)
            tile.Opacity = 0.0;
    }
    
        
        private void StartDropZoneFloatAnimation()
        {
            if (_newSectionDropZone == null)
                return;

            var transform = _newSectionDropZone.RenderTransform
                as TranslateTransform;

            if (transform == null)
            {
                transform = new TranslateTransform(0, 0);
                _newSectionDropZone.RenderTransform = transform;
            }

            transform.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = -5,
                Duration = TimeSpan.FromMilliseconds(1100),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                EasingFunction = new System.Windows.Media.Animation.SineEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                }
            };

            transform.BeginAnimation(
                TranslateTransform.YProperty,
                animation);
        }
               
        
            private void AnimateDropRipple(AppShortcut shortcut)
            {
                var tile = FindTileByShortcut(SectionsPanel, shortcut);

                if (tile == null || tile.Child is not Grid content)
                    return;

                // Clear any previous opacity animation so dragging can control opacity.
                tile.BeginAnimation(
                    UIElement.OpacityProperty,
                    null);

                tile.Opacity = 1.0;

                // Preserve the tile's existing hover transform.
                var group = tile.RenderTransform as TransformGroup;

                if (group == null)
                    return;

                var translate = group.Children
                    .OfType<TranslateTransform>()
                    .FirstOrDefault();

                if (translate == null)
                    return;

                translate.BeginAnimation(
                    TranslateTransform.YProperty, null);

                translate.Y = 6;

                var slideDuration = TimeSpan.FromMilliseconds(180);

                var slideY = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 6,
                    To = 0,
                    Duration = new Duration(slideDuration),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode =
                            System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };

                slideY.Completed += (_, _) =>
                {
                    translate.BeginAnimation(
                        TranslateTransform.YProperty, null);

                    translate.Y = 0;
                };

                translate.BeginAnimation(
                    TranslateTransform.YProperty, slideY);

                // Soft landing ripple.
                var ripple = new System.Windows.Shapes.Ellipse
                {
                    Stroke = new SolidColorBrush(
                        WpfColor.FromArgb(150, 190, 198, 208)),
                    StrokeThickness = 1.25,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    Width = 48,
                    Height = 48,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(0.35, 0.35),
                    Opacity = 0.65
                };

                content.Children.Add(ripple);
                System.Windows.Controls.Panel.SetZIndex(ripple, 100);

                var rippleDuration = new Duration(
                    TimeSpan.FromMilliseconds(280));

                var scaleX = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.35,
                    To = 1.3,
                    Duration = rippleDuration,
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode =
                            System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };

                var scaleY = scaleX.Clone();

                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.65,
                    To = 0,
                    Duration = rippleDuration,
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode =
                            System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };

                fadeOut.Completed += (_, _) =>
                {
                    content.Children.Remove(ripple);
                };

                var scale = (ScaleTransform)ripple.RenderTransform;

                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
                ripple.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
    
            private void Section_DragLeave(
                object sender,
                System.Windows.DragEventArgs e)
            {
                // Intentionally empty.
                // Do not clear insertion previews here.
                // DragLeave can fire repeatedly during an active drag.
                // Preview cleanup is handled by the drag lifecycle.
            }
       
            private void NewSectionDropZone_DragLeave(
                object sender,
                System.Windows.DragEventArgs e)
            {
                if (_newSectionDropZone == null)
                    return;

                _newSectionDropZone.Background =
                    new SolidColorBrush(
                        WpfColor.FromArgb(150, 22, 24, 28));

                _newSectionDropZone.BorderBrush =
                    new SolidColorBrush(
                        WpfColor.FromArgb(100, 150, 150, 150));
            }

            private void UpdateFloatingDragVisual(object? sender, EventArgs e)
            {
                if (_floatingDragVisual == null)
                    return;

                var screenPoint = System.Windows.Forms.Cursor.Position;

                var cursor = _host.DragOverlay.PointFromScreen(
                    new System.Windows.Point(screenPoint.X, screenPoint.Y));

                System.Windows.Controls.Canvas.SetLeft(
                    _floatingDragVisual,
                    cursor.X - _floatingDragOffset.X);

                System.Windows.Controls.Canvas.SetTop(
                    _floatingDragVisual,
                    cursor.Y - _floatingDragOffset.Y);
            }

            
            private Task AnimateDragGhostBackAsync(FrameworkElement sourceTile)
            {
                if (_floatingDragVisual == null)
                    return Task.CompletedTask;

                var ghost = _floatingDragVisual;

                // Find the original tile's position inside the drag overlay.
                var origin = sourceTile.TransformToVisual(_host.DragOverlay)
                    .Transform(new System.Windows.Point(0, 0));

                var duration = TimeSpan.FromMilliseconds(220);

                var leftAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = origin.X,
                    Duration = new System.Windows.Duration(duration),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };

                var topAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = origin.Y,
                    Duration = new System.Windows.Duration(duration),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                leftAnimation.Completed += (_, _) =>
                    completion.TrySetResult(true);

                ghost.BeginAnimation(
                    System.Windows.Controls.Canvas.LeftProperty,
                    leftAnimation);

                ghost.BeginAnimation(
                    System.Windows.Controls.Canvas.TopProperty,
                    topAnimation);

                return completion.Task;
            }


            private FrameworkElement BuildDragGhost(
            AppShortcut shortcut,
            double width,
            double height)
        {
            var ghost = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(
                    WpfColor.FromRgb(51, 55, 63)),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Opacity = 0.92,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };

            var content = new Grid
            {
                Margin = new Thickness(3, 5, 3, 4)
            };

            content.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });

            content.Children.Add(new System.Windows.Controls.Image
            {
                Source = shortcut.CachedIcon,
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

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

            var overlay = new Grid
            {
                IsHitTestVisible = false,
                ClipToBounds = true
            };

            overlay.Children.Add(content);

            Border CreateTrace()
            {
                var trace = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1.4),
                    BorderBrush = new SolidColorBrush(
                        WpfColor.FromArgb(230, 248, 250, 255)),
                    Background = System.Windows.Media.Brushes.Transparent,
                    IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = WpfColor.FromRgb(235, 242, 255),
                        BlurRadius = 6,
                        ShadowDepth = 0,
                        Opacity = 0.4
                    }
                };

                var opacityAnimation =
                    new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0.25,
                        To = 0.95,
                        Duration = TimeSpan.FromMilliseconds(700),
                        AutoReverse = true,
                        RepeatBehavior =
                            System.Windows.Media.Animation.RepeatBehavior.Forever
                    };

                trace.BeginAnimation(
                    UIElement.OpacityProperty,
                    opacityAnimation);

                return trace;
            }

            overlay.Children.Add(CreateTrace());
            overlay.Children.Add(CreateTrace());

            ghost.Child = overlay;

            return ghost;
        }
            
            private void DockWindow_GiveFeedback(
                object sender,
                System.Windows.GiveFeedbackEventArgs e)
            {
                UpdateFloatingDragVisual(null, EventArgs.Empty);
            }

            private void UpdateDragAutoScroll(System.Windows.DragEventArgs e)
            {
                if (!_dragStarted)
                    return;

                _dragPointerY = e.GetPosition(AppScrollViewer).Y;

                if (_dragAutoScrollTimer == null)
                {
                    _dragAutoScrollTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(8)
                    };

                    _dragAutoScrollTimer.Tick += (_, _) =>
                    {
                        if (!_dragStarted || !AppScrollViewer.IsVisible)
                        {
                            _dragAutoScrollTimer.Stop();
                            return;
                        }

                        double height = AppScrollViewer.ViewportHeight;
                        double maxOffset = Math.Max(
                            0,
                            AppScrollViewer.ExtentHeight - height);

                        const double edgeSize = 80;
                        double y = _dragPointerY;
                        double speed = 0;

                        if (y < edgeSize)
                        {
                            double intensity = Math.Clamp(
                                (edgeSize - y) / edgeSize, 0, 1);

                            speed = -700 * intensity;
                        }
                        else if (y > height - edgeSize)
                        {
                            double intensity = Math.Clamp(
                                (y - (height - edgeSize)) / edgeSize, 0, 1);

                            speed = 700 * intensity;
                        }

                        if (Math.Abs(speed) < 1)
                            return;

                        var now = DateTime.UtcNow;

                        double elapsed = _lastDragAutoScrollTick == DateTime.MinValue
                            ? 0.008
                            : Math.Clamp(
                                (now - _lastDragAutoScrollTick).TotalSeconds,
                                0,
                                0.025);

                        _lastDragAutoScrollTick = now;

                        double next = Math.Clamp(
                            AppScrollViewer.VerticalOffset + speed * elapsed,
                            0,
                            maxOffset);

                        AppScrollViewer.ScrollToVerticalOffset(next);
                    };
                }

                if (!_dragAutoScrollTimer.IsEnabled)
                    _dragAutoScrollTimer.Start();
            }
            

        private (string Category, int Slot)? GetDragInsertionPosition(
            object sender,
            System.Windows.DragEventArgs e,
            AppShortcut dragged)
        {
            if (sender is not FrameworkElement target)
                return null;

            var grid = target as System.Windows.Controls.Primitives.UniformGrid
                ?? FindParentUniformGrid(target);

            if (grid == null)
                return null;

            string category = NormalizeCategory(grid.Tag as string ?? "");

            if (string.IsNullOrWhiteSpace(category))
                return null;

            const double cellHeight = 82.0;
            int columns = Math.Max(1, grid.Columns);
            double cellWidth = grid.ActualWidth / columns;

            if (cellWidth <= 0)
                return null;

            var point = e.GetPosition(grid);

            // Work in actual grid slots, including gaps.
            int column = Math.Clamp(
                (int)(point.X / cellWidth), 0, columns - 1);

            int row = Math.Max(0, (int)(point.Y / cellHeight));
            double localX = point.X - column * cellWidth;
            double localY = point.Y - row * cellHeight;

            int hoveredSlot = row * columns + column;

            var hoveredApp = _shortcuts.FirstOrDefault(a =>
                !IsSectionMarker(a) &&
                a != dragged &&
                string.Equals(
                    NormalizeCategory(a.Category),
                    category,
                    StringComparison.OrdinalIgnoreCase) &&
                a.SlotIndex == hoveredSlot);

            // Empty cell: preview the exact slot. Do not compact the list.
            if (hoveredApp == null)
                return (category, hoveredSlot);

            // Lower half of an occupied cell: insert into the next row,
            // keeping the same column when possible.
            if (localY >= cellHeight * 0.80)
            return (category, (row + 1) * columns + column);

            // Left/right halves of an occupied app select before/after.
            if (localX < cellWidth / 2.0)
                return (category, hoveredSlot);

            return (category, hoveredSlot + 1);
        }   
        
        private void UpdateInsertionPreview(
            System.Windows.Controls.Primitives.UniformGrid grid,
            AppShortcut dragged,
            int insertSlot)
        {
            const double cellSize = 82.0;
            int columns = Math.Max(1, grid.Columns);
            string category = NormalizeCategory(grid.Tag as string ?? "");

            var apps = _shortcuts
                .Where(a =>
                    !IsSectionMarker(a) &&
                    a != dragged &&
                    string.Equals(
                        NormalizeCategory(a.Category),
                        category,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            var occupied = apps
                .Where(a => a.SlotIndex >= 0)
                .ToDictionary(a => a.SlotIndex, a => a);

            bool targetOccupied = occupied.ContainsKey(insertSlot);
            int leftGap = -1;

            int baseSlotCount = grid.Children
                .OfType<FrameworkElement>()
                .Count(element =>
                    !Equals(element.Tag, "__DRAG_PREVIEW_ROW__"));

            int highestPreviewSlot = -1;
            var reservedPreviewSlots = new HashSet<int>();

            // Find the nearest empty slot before the insertion point.
            for (int slot = insertSlot - 1; slot >= 0; slot--)
            {
                if (!occupied.ContainsKey(slot))
                {
                    leftGap = slot;
                    break;
                }
            }

            bool shiftLeft = targetOccupied && leftGap >= 0;

            foreach (UIElement child in grid.Children)
            {
                if (child is not FrameworkElement element)
                    continue;

                if (element.Tag is not AppShortcut app || app == dragged)
                {
                    if (element.RenderTransform is TransformGroup)
                        AnimateTileOffset(element, 0, 0);

                    continue;
                }

                int currentSlot = app.SlotIndex >= 0
                    ? app.SlotIndex
                    : app.OrderIndex;

                int previewSlot = currentSlot;

                if (shiftLeft)
                {
                    if (currentSlot > leftGap && currentSlot <= insertSlot)
                        previewSlot = currentSlot - 1;
                }
                else if (targetOccupied && currentSlot >= insertSlot)
                {
                    bool hasContinuousPath = true;

                    for (int slot = insertSlot; slot < currentSlot; slot++)
                    {
                        if (!occupied.ContainsKey(slot))
                        {
                            hasContinuousPath = false;
                            break;
                        }
                    }

                    if (hasContinuousPath)
                        previewSlot = currentSlot + 1;
                }

                // Never allow two previewed apps to occupy the same destination.
                    while (!reservedPreviewSlots.Add(previewSlot))
                    {
                        previewSlot++;
                    }

                    highestPreviewSlot = Math.Max(highestPreviewSlot, previewSlot);

                double offsetX =
                    ((previewSlot % columns) - (currentSlot % columns)) * cellSize;

                double offsetY =
                    ((previewSlot / columns) - (currentSlot / columns)) * cellSize;

                AnimateTileOffset(element, offsetX, offsetY);
            }

            // Animate the temporary preview row.
            bool needsNewRow = highestPreviewSlot >= baseSlotCount;

            AnimatePreviewRow(
                grid,
                needsNewRow,
                columns,
                baseSlotCount);
        }

        private void ClearInsertionPreview(
            System.Windows.Controls.Primitives.UniformGrid grid)
        {
            foreach (UIElement child in grid.Children)
            {
                if (child is FrameworkElement element &&
                    element.Tag is AppShortcut app &&
                    app != _previewDraggedShortcut)
                {
                    AnimateTileOffset(element, 0, 0);
                }
            }

            int baseSlotCount = grid.Children
                .OfType<FrameworkElement>()
                .Count(element =>
                    !Equals(element.Tag, "__DRAG_PREVIEW_ROW__"));

            AnimatePreviewRow(
                grid,
                false,
                Math.Max(1, grid.Columns),
                baseSlotCount);

            if (ReferenceEquals(_previewGrid, grid))
            {
                _previewGrid = null;
                _previewDraggedShortcut = null;
                _previewCategory = null;
                _previewInsertSlot = -1;
            }
        }

        private void AnimatePreviewRow(
            System.Windows.Controls.Primitives.UniformGrid grid,
            bool needsNewRow,
            int columns,
            int baseSlotCount)
        {
            
            var previewSlots = grid.Children
                .OfType<FrameworkElement>()
                .Where(element =>
                    Equals(element.Tag, "__DRAG_PREVIEW_ROW__"))
                .ToList();

            bool shouldAdd = needsNewRow && previewSlots.Count == 0;
            bool shouldRemove = !needsNewRow && previewSlots.Count > 0;

            if (!shouldAdd && !shouldRemove)
                return;

            var sectionsGrid = SectionsPanel?.Children
                .OfType<Grid>()
                .FirstOrDefault();

            FrameworkElement? affectedCell = null;

            if (sectionsGrid != null)
            {
                DependencyObject current = grid;

                while (current != null)
                {
                    var parent =
                        System.Windows.Media.VisualTreeHelper.GetParent(current);

                    if (ReferenceEquals(parent, sectionsGrid))
                    {
                        affectedCell = current as FrameworkElement;
                        break;
                    }

                    current = parent;
                }
            }

            int affectedRow = affectedCell != null
                ? Grid.GetRow(affectedCell)
                : -1;

            // Remember the current visual positions of all sections below.
            var previousPositions = new Dictionary<FrameworkElement, double>();

            if (sectionsGrid != null && affectedRow >= 0)
            {
                foreach (var cell in sectionsGrid.Children
                    .OfType<FrameworkElement>()
                    .Where(element => Grid.GetRow(element) > affectedRow))
                {
                    previousPositions[cell] = cell
                        .TransformToAncestor(sectionsGrid)
                        .Transform(new System.Windows.Point(0, 0)).Y;

                    // Remove any previous movement animation before measuring
                    // the new layout position.
                    if (cell.RenderTransform is TranslateTransform transform)
                    {
                        transform.BeginAnimation(
                            TranslateTransform.YProperty, null);

                        transform.Y = 0;
                    }
                }
            }

            // Add or remove the temporary row.
            if (shouldAdd)
            {
                for (int i = 0; i < columns; i++)
                {
                    grid.Children.Add(new Border
                    {
                        Width = 76,
                        Height = 76,
                        Margin = new Thickness(3),
                        Background = System.Windows.Media.Brushes.Transparent,
                        IsHitTestVisible = false,
                        Tag = "__DRAG_PREVIEW_ROW__"
                    });
                }
            }
            else
            {
                foreach (var slot in previewSlots)
                    grid.Children.Remove(slot);
            }

            // Let WPF calculate the new natural section layout.
            sectionsGrid?.UpdateLayout();

            // Animate the sections from their previous positions
            // into their new layout positions.
            foreach (var entry in previousPositions)
            {
                var cell = entry.Key;

                double newY = cell
                    .TransformToAncestor(sectionsGrid)
                    .Transform(new System.Windows.Point(0, 0)).Y;

                double offsetY = entry.Value - newY;

                if (Math.Abs(offsetY) < 0.5)
                    continue;

                var transform = cell.RenderTransform as TranslateTransform;

                if (transform == null)
                {
                    transform = new TranslateTransform();
                    cell.RenderTransform = transform;
                }

                double currentY = offsetY;

                transform.Y = 0;

                var animation = new DoubleAnimation
                {
                    From = currentY,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(260),
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseInOut
                    },
                    FillBehavior = FillBehavior.Stop
                };

                transform.BeginAnimation(
                    TranslateTransform.YProperty,
                    animation,
                    HandoffBehavior.SnapshotAndReplace);
            }
        }
        
        private void AnimateTileOffset(
            FrameworkElement element,
            double targetX,
            double targetY)
        {
            if (element.RenderTransform is not TransformGroup group)
                return;

            var transforms = group.Children
                .OfType<TranslateTransform>()
                .ToList();

            // The second-to-last translation belongs exclusively to preview movement.
            if (transforms.Count < 3)
                return;

            var transform = transforms[^2];

            // Capture the current animated position before changing base values.
            double currentX = transform.X;
            double currentY = transform.Y;

            double baseX = (double)transform.GetAnimationBaseValue(
                TranslateTransform.XProperty);

            double baseY = (double)transform.GetAnimationBaseValue(
                TranslateTransform.YProperty);

            // Avoid restarting an animation when the destination hasn't changed.
            if (Math.Abs(baseX - targetX) < 0.1 &&
                Math.Abs(baseY - targetY) < 0.1)
                return;

            // Update the underlying destination values.
            transform.X = targetX;
            transform.Y = targetY;

            var duration = TimeSpan.FromMilliseconds(210);

            var easing = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            };

            var xAnimation = new DoubleAnimation
            {
                From = currentX,
                To = targetX,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };

            var yAnimation = new DoubleAnimation
            {
                From = currentY,
                To = targetY,
                Duration = duration,
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };

            transform.BeginAnimation(
                TranslateTransform.XProperty,
                xAnimation,
                HandoffBehavior.SnapshotAndReplace);

            transform.BeginAnimation(
                TranslateTransform.YProperty,
                yAnimation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private System.Windows.Controls.Primitives.UniformGrid?
            FindParentUniformGrid(DependencyObject element)
        {
            while (element != null)
            {
                if (element is System.Windows.Controls.Primitives.UniformGrid grid)
                    return grid;

                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }

            return null;
        }


        private void Section_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        UpdateDragAutoScroll(e);

        if (_dragStarted &&
            e.Data.GetDataPresent("KageDockShortcut") &&
            e.Data.GetData("KageDockShortcut") is AppShortcut dragged)
        {
            var insertion = GetDragInsertionPosition(sender, e, dragged);

            if (insertion.HasValue)
            {
                string newCategory = insertion.Value.Category;
                int newSlot = insertion.Value.Slot;

                bool insertionChanged =
                    _previewDraggedShortcut != dragged ||
                    !string.Equals(
                        _previewCategory,
                        newCategory,
                        StringComparison.OrdinalIgnoreCase) ||
                    _previewInsertSlot != newSlot;

                var targetGrid =
                    sender is FrameworkElement target
                        ? target as System.Windows.Controls.Primitives.UniformGrid
                            ?? FindParentUniformGrid(target)
                        : null;

                bool gridChanged = targetGrid != null &&
                    !ReferenceEquals(_previewGrid, targetGrid);
                if ((insertionChanged || gridChanged) &&
                    targetGrid != null)
                {
                    if (_previewGrid != null &&
                        !ReferenceEquals(_previewGrid, targetGrid))
                    {
                        ClearInsertionPreview(_previewGrid);
                    }

                    _previewGrid = targetGrid;
                    _previewDraggedShortcut = dragged;
                    _previewCategory = newCategory;
                    _previewInsertSlot = newSlot;

                    UpdateInsertionPreview(
                        targetGrid,
                        dragged,
                        newSlot);
                }
            }
        }

        if (e.Data.GetDataPresent("KageDockShortcut"))
        {
            e.Effects = System.Windows.DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }

        if (ReferenceEquals(sender, _newSectionDropZone) &&
            e.Data.GetDataPresent("KageDockShortcut"))
        {
            _newSectionDropZone.Background =
                new SolidColorBrush(
                    WpfColor.FromArgb(90, 210, 210, 210));

            _newSectionDropZone.BorderBrush =
                new SolidColorBrush(
                    WpfColor.FromArgb(200, 225, 225, 225));
        }

        e.Handled = true;
    }
    
        private void Section_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (sender is not FrameworkElement target)
                return;

            // Handle dragging an existing app.
            if (e.Data.GetDataPresent("KageDockShortcut") &&
                e.Data.GetData("KageDockShortcut") is AppShortcut dragged)
            {
                if (target.Tag is int targetSlot &&
                    target.Parent is System.Windows.Controls.Panel parentPanel &&
                    parentPanel.Tag is string slotCategory)
                {
                    string category = NormalizeCategory(slotCategory);

                    var occupiedApp = _shortcuts.FirstOrDefault(a =>
                        !IsSectionMarker(a) &&
                        a != dragged &&
                        string.Equals(
                            NormalizeCategory(a.Category),
                            category,
                            StringComparison.OrdinalIgnoreCase) &&
                        a.SlotIndex == targetSlot);

                    // This handler is for an actually empty slot only.
                    if (occupiedApp == null)
                    {
                        // Keep the old slot vacant. Do not compact other apps.
                        _shortcutCoordinator!.MoveToSlot(
                            dragged,
                            category,
                            targetSlot);

                        SaveAndRefresh();

                        _sectionsRefreshedDuringDrag = true;

                        _pendingDropRippleShortcut = dragged;

                        e.Handled = true;
                        return;
                    }
                }

                // Create a new section ONLY when dropped on a dedicated drop zone.
                string? dropZoneTag = target.Tag as string;

                if (dropZoneTag == "__NEW_GROUP_DROP__" ||
                    (dropZoneTag != null &&
                    dropZoneTag.StartsWith(
                        "__NEW_GROUP_DROP__:",
                        StringComparison.Ordinal)))
                {
                    _shortcutCoordinator!.MoveToNewSection(dragged);
                    SaveAndRefresh();

                    _sectionsRefreshedDuringDrag = true;

                    e.Handled = true;
                    return;
                }

                // Resolve the section from the drop target.
                string targetCategory =
                    target.Tag is AppShortcut targetShortcut
                        ? NormalizeCategory(targetShortcut.Category)
                        : target.Tag is string targetTag
                            ? NormalizeCategory(targetTag)
                            : target is System.Windows.Controls.Panel panel &&
                            panel.Tag is string panelCategory
                                ? NormalizeCategory(panelCategory)
                                : "";

                // Other unrecognized drops do nothing.
                if (string.IsNullOrWhiteSpace(targetCategory))
                {
                    e.Effects = System.Windows.DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                var destinationApps = _shortcuts
                    .Where(a =>
                        !IsSectionMarker(a) &&
                        a != dragged &&
                        string.Equals(
                            NormalizeCategory(a.Category),
                            targetCategory,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int insertSlot;

                if (_previewDraggedShortcut == dragged &&
                    string.Equals(
                        _previewCategory,
                        targetCategory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // If dropped directly back onto the original app tile,
                    // cancel the drop without changing any slots.
                    if (target.Tag is AppShortcut targetApp &&
                        targetApp == dragged)
                    {
                        if (_previewGrid != null)
                            ClearInsertionPreview(_previewGrid);

                        if (_dragSourceTile != null)
                            _dragSourceTile.RenderTransform = Transform.Identity;

                        e.Handled = true;
                        return;
                    }

                    insertSlot = _previewInsertSlot;
                }
                else if (target.Tag is AppShortcut tileShortcut &&
                        tileShortcut != dragged)
                {
                    int tileSlot = tileShortcut.SlotIndex;

                    insertSlot = tileSlot < 0
                        ? destinationApps.Count
                        : e.GetPosition(target).X < target.ActualWidth / 2.0
                            ? tileSlot
                            : tileSlot + 1;
                }
                else if (target.Tag is int emptySlot)
                {
                    insertSlot = Math.Clamp(
                        emptySlot,
                        0,
                        destinationApps.Count);
                }
                else
                {
                    insertSlot = destinationApps.Count;
                }

                _shortcutCoordinator!.MoveToInsertionSlot(
                    dragged,
                    targetCategory,
                    insertSlot,
                    destinationApps);

                SaveAndRefresh();

                _sectionsRefreshedDuringDrag = true;

                e.Handled = true;
                return;
            }

            // Handle files dragged from Windows Explorer.
            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            {
                string category = target.Tag as string
                    ?? (target is System.Windows.Controls.Panel panel &&
                        panel.Tag is string panelCategory
                            ? panelCategory
                            : "Pinned");

                if (target == SectionsPanel)
                    category = "Pinned";

                _shortcutCoordinator!.AddFiles(paths, category);

                SaveAndRefresh();
                e.Handled = true;
            }
        }
    }
}