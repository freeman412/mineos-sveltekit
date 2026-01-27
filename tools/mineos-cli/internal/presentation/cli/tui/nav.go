package tui

// BuildNavItems creates the full navigation menu
func BuildNavItems() []NavItem {
	return []NavItem{
		// Views section
		{Label: "VIEWS", ItemType: NavHeader},
		{Label: "Dashboard", ItemType: NavView, View: ViewDashboard},
		{Label: "Minecraft Servers", ItemType: NavView, View: ViewServers},
		{Label: "Service Logs", ItemType: NavView, View: ViewServiceLogs},
		{Label: "Settings", ItemType: NavView, View: ViewSettings},

		{Label: "", ItemType: NavSeparator},

		// Docker services (containers)
		{Label: "SERVICES", ItemType: NavHeader},
		{Label: "Start Services", ItemType: NavAction, Action: &MenuItem{Label: "Start Services", Args: []string{"stack", "up"}}},
		{Label: "Stop Services", ItemType: NavAction, Action: &MenuItem{Label: "Stop Services", Args: []string{"stack", "stop"}}},
		{Label: "Restart Services", ItemType: NavAction, Action: &MenuItem{Label: "Restart Services", Args: []string{"stack", "restart"}}},
		{Label: "Stop & Remove", ItemType: NavAction, Action: &MenuItem{Label: "Stop & Remove", Args: []string{"stack", "down"}}, Destructive: true},
		{Label: "Pull Images", ItemType: NavAction, Action: &MenuItem{Label: "Pull Images", Args: []string{"stack", "pull"}}},
		{Label: "Rebuild", ItemType: NavAction, Action: &MenuItem{Label: "Rebuild", Args: []string{"stack", "rebuild"}}},
		{Label: "Update", ItemType: NavAction, Action: &MenuItem{Label: "Update", Args: []string{"stack", "update"}}},

		{Label: "", ItemType: NavSeparator},

		// System actions
		{Label: "SYSTEM", ItemType: NavHeader},
		{Label: "Reconfigure", ItemType: NavAction, Action: &MenuItem{Label: "Reconfigure", Args: []string{"reconfigure"}, Interactive: true}},
		{Label: "Refresh API Key", ItemType: NavAction, Action: &MenuItem{Label: "Refresh API Key", Args: []string{"api-key", "refresh"}}},
		{Label: "Install", ItemType: NavAction, Action: &MenuItem{Label: "Install", Args: []string{"install"}, Interactive: true}},
		{Label: "Uninstall", ItemType: NavAction, Action: &MenuItem{Label: "Uninstall", Args: []string{"uninstall"}, Interactive: true}},
	}
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
