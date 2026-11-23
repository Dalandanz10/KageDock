
using System.Windows.Controls;

namespace KageDock.Contracts
{
    public interface IMyAppsHost
    {
        // Shell-owned drag overlay.
        Canvas DragOverlay { get; }

        // Shell state.
        bool IsDockExpanded { get; }
         bool IsContextMenuOpen { get; }

        void SetContextMenuOpen(bool isOpen);
        bool IsChildWindowVisible { get; }
        bool IsWindowMouseOver { get; }
        bool IsDockPanelMouseOver { get; }
        bool IsTopHoverTriggerMouseOver { get; }

        // Shell actions requested by My Apps.
        void CollapseDock();
        void ScheduleCollapse();
        void CancelCollapseTimer();

        // My Apps drag lifecycle.
        void SetMyAppsDragActive(bool isActive);
    }
}
