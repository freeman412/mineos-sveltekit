package tui

import "os"

// BuildNavItems creates the full navigation menu
func BuildNavItems() []NavItem {
	items := []NavItem{
		// Views section
		{Label: "VIEWS", ItemType: NavHeader},
		{Label: "Dashboard", ItemType: NavView, View: ViewDashboard},
		{Label: "Minecraft Servers", ItemType: NavView, View: ViewServers},
		{Label: "Service Logs", ItemType: NavView, View: ViewServiceLogs},
		{Label: "Settings", ItemType: NavView, View: ViewSettings},

		{Label: "", ItemType: NavSeparator},

		// Docker services (containers)
		{Label: "DOCKER", ItemType: NavHeader},
		{Label: "Start Containers", ItemType: NavAction, Action: &MenuItem{Label: "Start Containers", Args: []string{"stack", "up"}, Streaming: true}},
		{Label: "Stop Containers", ItemType: NavAction, Action: &MenuItem{Label: "Stop Containers", Args: []string{"stack", "stop"}, Streaming: true}},
		{Label: "Restart Containers", ItemType: NavAction, Action: &MenuItem{Label: "Restart Containers", Args: []string{"stack", "restart"}, Streaming: true}},
		{Label: "Remove Containers", ItemType: NavAction, Action: &MenuItem{Label: "Remove Containers", Args: []string{"stack", "down"}, Destructive: true, Streaming: true}, Destructive: true},
		{Label: "Update Images", ItemType: NavAction, Action: &MenuItem{Label: "Update Images", Args: []string{"stack", "update"}, Streaming: true}},
	}

	// Only show Rebuild if source code is available (apps directory exists)
	if hasSourceCode() {
		items = append(items, NavItem{Label: "Rebuild Source", ItemType: NavAction, Action: &MenuItem{Label: "Rebuild Source", Args: []string{"stack", "rebuild"}, Streaming: true}})
	}

	items = append(items,
		NavItem{Label: "", ItemType: NavSeparator},

		// System actions
		NavItem{Label: "SYSTEM", ItemType: NavHeader},
		NavItem{Label: "Install", ItemType: NavAction, Action: &MenuItem{Label: "Install", Args: []string{"install"}, Interactive: true}},
		NavItem{Label: "Upgrade CLI", ItemType: NavAction, Action: &MenuItem{Label: "Upgrade CLI", Args: []string{"upgrade"}}},
		NavItem{Label: "Reconfigure", ItemType: NavAction, Action: &MenuItem{Label: "Reconfigure", Args: []string{"reconfigure"}, Interactive: true}},
		NavItem{Label: "Refresh API Key", ItemType: NavAction, Action: &MenuItem{Label: "Refresh API Key", Args: []string{"api-key", "refresh"}}},
		NavItem{Label: "Uninstall", ItemType: NavAction, Action: &MenuItem{Label: "Uninstall", Args: []string{"uninstall"}, Interactive: true, Destructive: true}, Destructive: true},
	)

	return items
}

// hasSourceCode checks if the source code is available (apps directory exists)
func hasSourceCode() bool {
	info, err := os.Stat("apps")
	return err == nil && info.IsDir()
}

// IsSelectable returns true if the nav item can be selected
func (n NavItem) IsSelectable() bool {
	return n.ItemType == NavView || n.ItemType == NavAction
}

// NextSelectableIndex finds the next selectable item index
func NextSelectableIndex(items []NavItem, current int, direction int) int {
	next := current + direction
	for next >= 0 && next < len(items) {
		if items[next].IsSelectable() {
			return next
		}
		next += direction
	}
	return current
}

// FirstSelectableIndex returns the first selectable item index
func FirstSelectableIndex(items []NavItem) int {
	for i, item := range items {
		if item.IsSelectable() {
			return i
		}
	}
	return 0
}
