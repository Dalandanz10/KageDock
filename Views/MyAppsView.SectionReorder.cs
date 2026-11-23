using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;


namespace KageDock.Views
{
    public partial class MyAppsView
    {
        // Section reordering state. Completely separate from the app drag
        // state (_dragStarted, _pendingDragShortcut, ...).
        private string? _sectionPressCategory;              // header pressed (pending or dragging)
        private System.Windows.Point _sectionPressPoint;    // press position in SectionsPanel coordinates
        private Grid? _sectionDragCell;                     // non-null only while dragging
        private int _sectionDragIndex;
        private int _sectionTargetIndex;
        private List<Grid> _sectionCells = new();
        private List<System.Windows.Rect> _sectionSlots = new();
        private List<int> _sectionNaturalRows = new();      // tile rows each section needs on its own
        private List<double> _sectionFixedHeights = new();  // cell height minus its tile rows
        private List<System.Windows.Point> _sectionHome = new();
        private DateTime _sectionDragLastFrame = DateTime.MinValue;
        private Window? _sectionDragWindow;

        private const double SectionTileRowHeight = 82.0;   // 76 px tile + 2 x 3 px margin

        // ---------------------------------------------------------------
        // Press / threshold / release
        // ---------------------------------------------------------------

        private void SectionHeader_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (_sectionDragCell != null ||
                _dragStarted ||
                _isReturningToTopForSectionRemoval)
            {
                return;
            }

            if (sender is not FrameworkElement header ||
                header.Tag is not string category)
            {
                return;
            }

            // Presses inside the inline rename editor must keep working.
            if (IsInsideTextBox(e.OriginalSource as DependencyObject))
                return;

            _sectionPressCategory = category;
            _sectionPressPoint = e.GetPosition(SectionsPanel);

            // Handlers exist only while a press or drag is in progress.
            SectionsPanel.PreviewMouseMove -= SectionPanel_PreviewMouseMove;
            SectionsPanel.PreviewMouseMove += SectionPanel_PreviewMouseMove;

            SectionsPanel.PreviewMouseLeftButtonUp -= SectionPanel_PreviewMouseLeftButtonUp;
            SectionsPanel.PreviewMouseLeftButtonUp += SectionPanel_PreviewMouseLeftButtonUp;

            SectionsPanel.LostMouseCapture -= SectionPanel_LostMouseCapture;
            SectionsPanel.LostMouseCapture += SectionPanel_LostMouseCapture;

            Unloaded -= SectionDrag_ViewUnloaded;
            Unloaded += SectionDrag_ViewUnloaded;

            IsVisibleChanged -= SectionDrag_ViewIsVisibleChanged;
            IsVisibleChanged += SectionDrag_ViewIsVisibleChanged;

            // Not handled and not captured yet: single click, double-click
            // rename and right-click rename keep working.
        }

        private void SectionPanel_PreviewMouseMove(
            object sender,
            System.Windows.Input.MouseEventArgs e)
        {
            // While dragging, the per-frame callback does the work.
            if (_sectionDragCell != null || _sectionPressCategory == null)
                return;

            // The button was released somewhere we did not see.
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                ReleaseSectionDragState();
                return;
            }

            var point = e.GetPosition(SectionsPanel);

            if (Math.Abs(point.X - _sectionPressPoint.X) <
                    SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _sectionPressPoint.Y) <
                    SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (!BeginSectionDrag())
                ReleaseSectionDragState();
        }

        private void SectionPanel_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (_sectionPressCategory == null)
                return;

            // Plain click on the header: nothing to do.
            if (_sectionDragCell == null)
            {
                ReleaseSectionDragState();
                return;
            }

            e.Handled = true;
            FinishSectionDrag(commit: true);
        }

        private void SectionPanel_LostMouseCapture(
            object sender,
            System.Windows.Input.MouseEventArgs e)
        {
            // Capture was taken away (focus change, deactivation, ...).
            if (_sectionDragCell != null)
                FinishSectionDrag(commit: false);
        }

        private void SectionDrag_PreviewKeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _sectionDragCell == null)
                return;

            e.Handled = true;
            FinishSectionDrag(commit: false);
        }

        // Anything that makes the drag meaningless ends it immediately.
        private void SectionDrag_ViewUnloaded(object sender, RoutedEventArgs e) =>
            CancelSectionDragImmediately();

        private void SectionDrag_ViewIsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
                CancelSectionDragImmediately();
        }

        private void SectionDrag_WindowDeactivated(object? sender, EventArgs e) =>
            CancelSectionDragImmediately();

        // ---------------------------------------------------------------
        // Drag start
        // ---------------------------------------------------------------

        private bool BeginSectionDrag()
        {
            var sectionsGrid =
                SectionsPanel.Children.OfType<Grid>().FirstOrDefault();

            if (sectionsGrid == null || _sectionPressCategory == null)
                return false;

            // Cells in layout order: row 0 left, row 0 right, row 1 left, ...
            var cells = sectionsGrid.Children
                .OfType<Grid>()
                .Where(c => c.Tag is string)
                .OrderBy(c => Grid.GetRow(c) * 2 + Grid.GetColumn(c))
                .ToList();

            int index = cells.FindIndex(c =>
                ((string)c.Tag).Equals(
                    _sectionPressCategory,
                    StringComparison.OrdinalIgnoreCase));

            // Nothing to reorder with a single section.
            if (index < 0 || cells.Count < 2)
                return false;

            // Fresh render-only transforms. This also drops any leftover
            // animation, so the measurements below are the real layout slots.
            foreach (var cell in cells)
            {
                ClearSectionTransform(cell);
                cell.RenderTransform = new TranslateTransform();
            }

            if (!SectionsPanel.CaptureMouse())
                return false;

            _sectionCells = cells;

            _sectionSlots = cells
                .Select(c => c
                    .TransformToAncestor(SectionsPanel)
                    .TransformBounds(new System.Windows.Rect(
                        0, 0, c.ActualWidth, c.ActualHeight)))
                .ToList();

            _sectionNaturalRows = cells
                .Select(c => GetNaturalSectionRows((string)c.Tag))
                .ToList();

            // Real cells are padded to the taller section of their pair.
            // Remove that padding to get the fixed part of each cell's height.
            _sectionFixedHeights = cells
                .Select((c, i) =>
                {
                    int pairRows = _sectionNaturalRows[i];
                    int partner = i ^ 1;

                    if (partner < cells.Count)
                        pairRows = Math.Max(pairRows, _sectionNaturalRows[partner]);

                    return c.ActualHeight + c.Margin.Top + c.Margin.Bottom
                        - pairRows * SectionTileRowHeight;
                })
                .ToList();

            var identity = Enumerable.Range(0, cells.Count).ToList();

            _sectionHome = identity
                .Select(i => GetSectionSlotPosition(identity, i))
                .ToList();
            
            _sectionDragIndex = index;
            _sectionTargetIndex = index;
            _sectionDragCell = cells[index];
            _sectionDragLastFrame = DateTime.MinValue;

            // The lifted section draws above its neighbours.
            System.Windows.Controls.Panel.SetZIndex(_sectionDragCell, 100);

            SectionsPanel.Cursor = System.Windows.Input.Cursors.SizeAll;

            // Same shell flag the app drag uses (keeps the dock from collapsing).
            _host.SetMyAppsDragActive(true);

            _sectionDragWindow = Window.GetWindow(this);

            if (_sectionDragWindow != null)
            {
                _sectionDragWindow.PreviewKeyDown += SectionDrag_PreviewKeyDown;
                _sectionDragWindow.Deactivated += SectionDrag_WindowDeactivated;
            }

            CompositionTarget.Rendering += SectionDrag_Rendering;

            return true;
        }

        // ---------------------------------------------------------------
        // Per-frame update (only runs while dragging)
        // ---------------------------------------------------------------

        private void SectionDrag_Rendering(object? sender, EventArgs e)
        {
            if (_sectionDragCell == null)
                return;

            // The view or dock went away mid-drag. If IsDockExpanded is ever
            // false while the dock is open, delete just that last condition.
            if (!IsLoaded || !IsVisible || !_host.IsDockExpanded)
            {
                CancelSectionDragImmediately();
                return;
            }

            AutoScrollSectionDrag();
            UpdateSectionDrag();
        }

        private void UpdateSectionDrag()
        {
            if (_sectionDragCell?.RenderTransform is not TranslateTransform ghost)
                return;

            var pointer = Mouse.GetPosition(SectionsPanel);

            // The lifted section follows the pointer in content coordinates,
            // so it stays under the cursor while the view scrolls.
            ghost.X = pointer.X - _sectionPressPoint.X;
            ghost.Y = pointer.Y - _sectionPressPoint.Y;

            int target = GetSectionSlotAt(pointer);

            if (target == _sectionTargetIndex)
                return;

            _sectionTargetIndex = target;
            ApplySectionPreview();
        }

        // Slot under the pointer, based on the original (static) slot
        // rectangles. Gaps and margins resolve to the nearest slot, so the
        // first, last and odd-last-row cases need no special handling.
        private int GetSectionSlotAt(System.Windows.Point point)
        {
            int best = _sectionTargetIndex;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < _sectionSlots.Count; i++)
            {
                var slot = _sectionSlots[i];

                double dx = Math.Max(
                    Math.Max(slot.Left - point.X, 0),
                    point.X - slot.Right);

                double dy = Math.Max(
                    Math.Max(slot.Top - point.Y, 0),
                    point.Y - slot.Bottom);

                double distance = dx * dx + dy * dy;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        // Where the cell shown in the given layout slot sits when the cells
        // appear in 'order'.
        private System.Windows.Point GetSectionSlotPosition(
            IReadOnlyList<int> order,
            int slot)
        {
            double y = _sectionSlots[0].Y;

            for (int row = 0; row < slot / 2; row++)
                y += GetSectionRowHeight(order, row);

            return new System.Windows.Point(_sectionSlots[slot % 2].X, y);
        }

        // Both cells in a row are padded to the taller section, and the
        // Auto row is as tall as that (including the cell margins).
        private double GetSectionRowHeight(IReadOnlyList<int> order, int row)
        {
            int first = order[row * 2];
            int second = row * 2 + 1 < order.Count ? order[row * 2 + 1] : -1;

            int rows = _sectionNaturalRows[first];
            double fixedHeight = _sectionFixedHeights[first];

            if (second >= 0)
            {
                rows = Math.Max(rows, _sectionNaturalRows[second]);
                fixedHeight = Math.Max(fixedHeight, _sectionFixedHeights[second]);
            }

            return fixedHeight + rows * SectionTileRowHeight;
        }

        // Same formula RefreshSections uses for sectionGridRows.
        private int GetNaturalSectionRows(string category)
        {
            var apps = _shortcuts
                .Where(s =>
                    !IsSectionMarker(s) &&
                    NormalizeCategory(s.Category).Equals(
                        category,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            int highestSlot = apps
                .Select(s => s.SlotIndex >= 0 ? s.SlotIndex : -1)
                .DefaultIfEmpty(-1)
                .Max();

            return Math.Max(1, (Math.Max(apps.Count, highestSlot + 1) + 3) / 4);
        }

        private void ApplySectionPreview()
        {
            int count = _sectionCells.Count;

            // Order of cells after moving the dragged one to the target slot.
            var order = Enumerable.Range(0, count)
                .Where(i => i != _sectionDragIndex)
                .ToList();

            order.Insert(_sectionTargetIndex, _sectionDragIndex);

            for (int slot = 0; slot < count; slot++)
            {
                int cellIndex = order[slot];

                if (cellIndex == _sectionDragIndex ||
                    _sectionCells[cellIndex].RenderTransform
                        is not TranslateTransform transform)
                {
                    continue;
                }

                var target = GetSectionSlotPosition(order, slot);
                var home = _sectionHome[cellIndex];

                AnimateSectionPreviewTo(
                    transform,
                    target.X - home.X,
                    target.Y - home.Y);
            }
        }

        private static void AnimateSectionPreviewTo(
            TranslateTransform transform,
            double x,
            double y)
        {
            var duration = TimeSpan.FromMilliseconds(220);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            // No 'From': a new target continues smoothly from the current value.
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(x, duration) { EasingFunction = easing });

            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(y, duration) { EasingFunction = easing });
        }

        private void AutoScrollSectionDrag()
        {
            var now = DateTime.UtcNow;

            double elapsed = _sectionDragLastFrame == DateTime.MinValue
                ? 0.016
                : Math.Clamp((now - _sectionDragLastFrame).TotalSeconds, 0, 0.05);

            _sectionDragLastFrame = now;

            double height = AppScrollViewerControl.ViewportHeight;
            double y = Mouse.GetPosition(AppScrollViewerControl).Y;

            const double edgeSize = 100;
            const double maxSpeed = 1200;

            double speed = 0;

            if (y < edgeSize)
            {
                speed = -maxSpeed * Math.Clamp((edgeSize - y) / edgeSize, 0, 1);
            }
            else if (y > height - edgeSize)
            {
                speed = maxSpeed * Math.Clamp(
                    (y - (height - edgeSize)) / edgeSize, 0, 1);
            }

            if (Math.Abs(speed) < 1)
                return;

            double maxOffset = Math.Max(
                0,
                AppScrollViewerControl.ExtentHeight -
                AppScrollViewerControl.ViewportHeight);

            double targetOffset = Math.Clamp(
                AppScrollViewerControl.VerticalOffset + speed * elapsed,
                0,
                maxOffset);

            if (Math.Abs(targetOffset - AppScrollViewerControl.VerticalOffset) > 0.5)
                AppScrollViewerControl.ScrollToVerticalOffset(targetOffset);
        }

        // ---------------------------------------------------------------
        // Drop / cancel / cleanup
        // ---------------------------------------------------------------

        private void FinishSectionDrag(bool commit)
        {
            var cells = _sectionCells;
            var dragCell = _sectionDragCell;
            int from = _sectionDragIndex;
            int to = _sectionTargetIndex;

            if (dragCell == null)
                return;

            // Releasing outside the view is an invalid drop: cancel.
            var viewPoint = Mouse.GetPosition(this);

            bool insideView =
                viewPoint.X >= 0 && viewPoint.Y >= 0 &&
                viewPoint.X <= ActualWidth && viewPoint.Y <= ActualHeight;

            // Clears state but deliberately leaves the transforms alone.
            ReleaseSectionDragState();

            // Never change the order if RefreshSections would decline to rebuild.
            if (commit &&
                insideView &&
                to != from &&
                !_isReturningToTopForSectionRemoval)
            {
                var others = cells
                    .Where(c => !ReferenceEquals(c, dragCell))
                    .Select(c => (string)c.Tag)
                    .ToList();

                // Final index 'to' == before others[0], or after others[to - 1].
                string neighbour = to == 0 ? others[0] : others[to - 1];

                if (_shortcutCoordinator.MoveSection(
                        (string)dragCell.Tag,
                        neighbour,
                        insertAfter: to != 0))
                {
                    // A reorder must never trigger the scroll-to-top transition.
                    _skipScrollToTopForSectionRemoval = true;

                    // Saves the new order, then rebuilds. RefreshSections
                    // animates every cell from where it is now to its slot.
                    SaveAndRefresh();

                    // The old cells have been replaced: release their clocks.
                    foreach (var cell in cells)
                        ClearSectionTransform(cell);

                    return;
                }
            }

            // Cancelled, invalid drop, dropped in place, or nothing changed.
            AnimateSectionDragCancel(cells);
        }

        private void AnimateSectionDragCancel(List<Grid> cells)
        {
            var duration = TimeSpan.FromMilliseconds(220);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            foreach (var cell in cells)
            {
                if (cell.RenderTransform is not TranslateTransform transform)
                    continue;

                var moveX = new DoubleAnimation(0, duration)
                {
                    EasingFunction = easing
                };

                var moveY = new DoubleAnimation(0, duration)
                {
                    EasingFunction = easing
                };

                moveY.Completed += (_, _) =>
                {
                    // Skip if a new drag already replaced this transform.
                    if (ReferenceEquals(cell.RenderTransform, transform))
                        ClearSectionTransform(cell);
                };

                transform.BeginAnimation(TranslateTransform.XProperty, moveX);
                transform.BeginAnimation(TranslateTransform.YProperty, moveY);
            }
        }

        // Called at the top of RefreshSections and from the guards above:
        // ends any press or drag at once, with no animation.
        private void CancelSectionDragImmediately()
        {
            if (_sectionPressCategory == null)
                return;

            var cells = _sectionCells;

            ReleaseSectionDragState();

            foreach (var cell in cells)
                ClearSectionTransform(cell);
        }

        // Single exit point: detaches everything and clears all state.
        private void ReleaseSectionDragState()
        {
            bool wasDragging = _sectionDragCell != null;

            CompositionTarget.Rendering -= SectionDrag_Rendering;

            SectionsPanel.PreviewMouseMove -= SectionPanel_PreviewMouseMove;
            SectionsPanel.PreviewMouseLeftButtonUp -= SectionPanel_PreviewMouseLeftButtonUp;
            SectionsPanel.LostMouseCapture -= SectionPanel_LostMouseCapture;

            Unloaded -= SectionDrag_ViewUnloaded;
            IsVisibleChanged -= SectionDrag_ViewIsVisibleChanged;

            if (_sectionDragWindow != null)
            {
                _sectionDragWindow.PreviewKeyDown -= SectionDrag_PreviewKeyDown;
                _sectionDragWindow.Deactivated -= SectionDrag_WindowDeactivated;
                _sectionDragWindow = null;
            }

            _sectionPressCategory = null;
            _sectionDragCell = null;

            // New lists (not Clear()): callers may still hold the old ones.
            _sectionCells = new List<Grid>();
            _sectionSlots = new List<System.Windows.Rect>();
            _sectionNaturalRows = new List<int>();
            _sectionFixedHeights = new List<double>();
            _sectionHome = new List<System.Windows.Point>();

            if (SectionsPanel.IsMouseCaptured)
                SectionsPanel.ReleaseMouseCapture();

            SectionsPanel.ClearValue(FrameworkElement.CursorProperty);

            if (!wasDragging)
                return;

            _host.SetMyAppsDragActive(false);

            // Same collapse recheck the app drag performs after it ends.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_host.IsDockExpanded &&
                    !_dragStarted &&
                    !_host.IsContextMenuOpen &&
                    !_host.IsChildWindowVisible &&
                    Mouse.LeftButton == MouseButtonState.Released &&
                    !_host.IsDockPanelMouseOver &&
                    !_host.IsTopHoverTriggerMouseOver)
                {
                    _host.ScheduleCollapse();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Clears preview animations first (so no hold-end clocks linger),
        // then returns the cell to a plain, untransformed state.
        private static void ClearSectionTransform(Grid cell)
        {
            if (cell.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.BeginAnimation(TranslateTransform.YProperty, null);
            }

            cell.RenderTransform = Transform.Identity;
            System.Windows.Controls.Panel.SetZIndex(cell, 0);
        }

        private static bool IsInsideTextBox(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is System.Windows.Controls.TextBox)
                    return true;

                element = element is Visual
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return false;
        }
    }
}