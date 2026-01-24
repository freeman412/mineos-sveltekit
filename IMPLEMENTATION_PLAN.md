# MineOS Feature Implementation Plan

This document outlines the implementation plan for the feature roadmap. Issues are organized by priority and dependencies.

## Summary of Created Issues

### High Priority (Now)
- [#15](https://github.com/freeman412/mineos-sveltekit/issues/15) - Add server address copy/share button
- [#16](https://github.com/freeman412/mineos-sveltekit/issues/16) - Upload and display Minecraft server icon
- [#17](https://github.com/freeman412/mineos-sveltekit/issues/17) - Implement keyboard shortcuts for navigation
- [#18](https://github.com/freeman412/mineos-sveltekit/issues/18) - Update favicon to match main logo block
- [#19](https://github.com/freeman412/mineos-sveltekit/issues/19) - Improve file editor theme to match site design
- [#20](https://github.com/freeman412/mineos-sveltekit/issues/20) - Add mod version compatibility checking and filtering
- [#21](https://github.com/freeman412/mineos-sveltekit/issues/21) - Implement automatic mod dependency checking and installation
- [#22](https://github.com/freeman412/mineos-sveltekit/issues/22) - Implement scheduled cron job backups for servers

### Future Priority
- [#23](https://github.com/freeman412/mineos-sveltekit/issues/23) - Refactor: Launch servers as separate Docker containers
- [#24](https://github.com/freeman412/mineos-sveltekit/issues/24) - Customize esheep with Minecraft character sprites
- [#25](https://github.com/freeman412/mineos-sveltekit/issues/25) - Add OAuth login support (Discord/Google)
- [#26](https://github.com/freeman412/mineos-sveltekit/issues/26) - Implement invite links with auto-whitelist

---

## Phase 1: Quick Wins (1-2 weeks)

These features provide immediate value with relatively low implementation complexity.

### 1.1 Visual & UX Polish
**Estimated Time: 2-3 days**

**Tasks:**
1. **Update Favicon (#18)** - 2 hours
   - Generate favicon in multiple sizes
   - Update app.html with new favicon links
   - Test across browsers

2. **Improve File Editor Theme (#19)** - 4 hours
   - Audit current editor styling
   - Extract site color variables
   - Apply theme to editor component
   - Test with different file types

3. **Add Server Address Copy Button (#15)** - 3 hours
   - Add copy button component to server detail page
   - Implement clipboard API
   - Add toast notification
   - Handle different server states (running/stopped)

**Dependencies:** None

**Test Plan:**
- Manual testing across major browsers
- Test clipboard functionality on different OSes
- Verify styling matches site theme

---

### 1.2 Keyboard Navigation
**Estimated Time: 1 day**

**Tasks:**
1. **Implement Keyboard Shortcuts (#17)** - 6-8 hours
   - Create global keyboard event listener
   - Define shortcut mappings (Shift+D, Shift+S, etc.)
   - Add shortcuts help dialog
   - Ensure shortcuts don't interfere with input fields
   - Add visual hints/tooltips

**Dependencies:** None

**Test Plan:**
- Test all shortcut combinations
- Verify shortcuts disabled in input fields
- Test help dialog display and navigation

---

## Phase 2: Server Management Features (2-3 weeks)

These features enhance core server management functionality.

### 2.1 Server Icon Upload
**Estimated Time: 3-4 days**

**Tasks:**
1. **Server Icon Upload (#16)** - 12-16 hours
   - Design upload UI component
   - Implement file upload endpoint (API)
   - Add image validation (64x64 PNG)
   - Add image cropping/resizing tool
   - Save as server-icon.png in server directory
   - Display icon in UI as banner/header
   - Add default icon fallback

**Dependencies:**
- May benefit from #19 (theme improvements) for UI consistency

**Technical Considerations:**
- Use library like cropperjs for image editing
- Validate file size and format server-side
- Consider rate limiting on upload endpoint

**Test Plan:**
- Upload valid 64x64 PNG
- Upload wrong size image (should crop/resize)
- Upload non-PNG file (should reject)
- Verify icon appears in-game and in UI

---

### 2.2 Automated Backups
**Estimated Time: 5-7 days**

**Tasks:**
1. **Implement Cron Backup System (#22)** - 20-28 hours
   - Design backup configuration UI
   - Add backup schedule fields to server settings
   - Implement background job scheduler (node-cron)
   - Create backup service (compress server directory)
   - Implement retention policy (cleanup old backups)
   - Add manual backup trigger button
   - Create backup restoration interface
   - Add backup history view
   - Implement Discord notifications (optional)
   - Add server pause during backup to prevent corruption

**Dependencies:** None (but pairs well with #15 for notifications)

**Technical Considerations:**
- Use streaming compression to handle large servers
- Implement proper file locking
- Consider backup during low-usage periods
- Ensure backups don't fill disk space

**Database Schema Changes:**
```sql
-- Add to server config or create new table
CREATE TABLE backup_schedules (
  id INTEGER PRIMARY KEY,
  server_id TEXT NOT NULL,
  enabled BOOLEAN DEFAULT TRUE,
  schedule TEXT NOT NULL, -- cron expression
  retention INTEGER DEFAULT 7,
  include_worlds_only BOOLEAN DEFAULT FALSE,
  compress BOOLEAN DEFAULT TRUE,
  last_run TIMESTAMP,
  next_run TIMESTAMP
);

CREATE TABLE backup_history (
  id INTEGER PRIMARY KEY,
  server_id TEXT NOT NULL,
  backup_path TEXT NOT NULL,
  size_bytes INTEGER,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  status TEXT DEFAULT 'success'
);
```

**Test Plan:**
- Create backup schedule
- Trigger manual backup
- Verify backup file integrity
- Test restoration process
- Verify retention policy cleanup
- Test with large servers

---

## Phase 3: Mod Management Enhancements (2-3 weeks)

These features significantly improve the mod installation experience.

### 3.1 Mod Version Compatibility
**Estimated Time: 4-5 days**

**Tasks:**
1. **Version Compatibility Checking (#20)** - 16-20 hours
   - Add Minecraft version detection from server
   - Parse version from server.properties or profile
   - Update mod search components (CurseForge & Modrinth)
   - Add compatibility badges to search results
   - Implement version filtering toggle
   - Show supported versions in mod detail view
   - Add warning dialog for incompatible mods
   - Handle version ranges (1.19.x, 1.20+, etc.)

**Dependencies:** None

**Technical Considerations:**
- Version comparison logic for Minecraft versions
- Handle snapshot versions (23w31a, etc.)
- Cache version compatibility data

**API Integration:**
- CurseForge: Filter by `gameVersions` field
- Modrinth: Filter by `game_versions` field

**Test Plan:**
- Test with servers running different MC versions
- Verify filtering works correctly
- Test edge cases (snapshots, modded versions)
- Verify UI updates dynamically based on server version

---

### 3.2 Automatic Dependency Resolution
**Estimated Time: 5-7 days**

**Tasks:**
1. **Mod Dependency Installation (#21)** - 20-28 hours
   - Fetch dependency data from CurseForge/Modrinth APIs
   - Build dependency tree (handle nested dependencies)
   - Create dependency confirmation dialog
   - Implement bulk mod download
   - Add progress indicator for multi-mod installation
   - Check for already-installed dependencies
   - Handle optional dependencies (user prompt)
   - Detect and warn about circular dependencies
   - Handle version conflicts

**Dependencies:**
- Should be implemented after #20 (version compatibility)
- Version compatibility is needed to select correct dependency versions

**Technical Considerations:**
- Implement breadth-first search for dependency resolution
- Set maximum dependency depth (prevent infinite loops)
- Cache dependency metadata to reduce API calls
- Handle API rate limits

**Test Plan:**
- Install mod with required dependencies
- Test nested dependencies (dep of dep)
- Test with already-installed dependencies
- Test circular dependency detection
- Verify optional dependency prompts

---

## Phase 4: Advanced Features (Future)

These are larger architectural changes or nice-to-have features for future implementation.

### 4.1 Docker Container Architecture
**Estimated Time: 4-6 weeks**

**Tasks:**
1. **Containerize Individual Servers (#23)**
   - Research and design architecture
   - Create base Minecraft server Docker image
   - Implement container lifecycle management
   - Handle dynamic port mapping
   - Update server state tracking
   - Migrate backup/restore logic
   - Add per-server resource limits
   - Update UI to show container status
   - Write migration guide
   - Test thoroughly with multiple servers

**Dependencies:**
- Should be implemented after core features are stable
- May require database schema changes

**Technical Considerations:**
- Major architectural change
- Requires careful planning and testing
- May break existing installations without migration

**Priority:** Low - Future enhancement

---

### 4.2 Authentication Enhancements
**Estimated Time: 2-3 weeks**

**Tasks:**
1. **OAuth Login Support (#25)** - 10-15 days
   - Register OAuth apps (Discord, Google)
   - Implement OAuth callback routes
   - Add login buttons to UI
   - Store OAuth tokens securely
   - Implement account linking
   - Update database schema
   - Document configuration process

**Dependencies:** None (standalone feature)

**Priority:** Low - Nice to have for convenience

---

### 4.3 Invite System
**Estimated Time: 2-3 weeks**

**Tasks:**
1. **Invite Links & Auto-Whitelist (#26)** - 10-15 days
   - Design invite system database schema
   - Create invite generation API
   - Build invite acceptance page
   - Implement whitelist automation via RCON
   - Add email invitation system (optional)
   - Create invite management UI
   - Add server accessibility check
   - Implement Discord notifications

**Dependencies:**
- Requires server RCON access
- Requires internet-accessible server

**Priority:** Low - Advanced multiplayer feature

---

### 4.4 Fun Additions
**Estimated Time: 1-2 days**

**Tasks:**
1. **Minecraft esheep Sprites (#24)** - 4-8 hours
   - Design or source Minecraft sprite sheets
   - Replace esheep graphics
   - Add character selection option
   - Test animations

**Dependencies:** None

**Priority:** Very Low - Cosmetic feature

---

## Recommended Implementation Order

Based on dependencies, value, and complexity:

### Sprint 1 (Week 1-2): Quick Wins
1. ✅ Update Favicon (#18) - 2 hours
2. ✅ Improve File Editor Theme (#19) - 4 hours
3. ✅ Add Server Address Copy Button (#15) - 3 hours
4. ✅ Implement Keyboard Shortcuts (#17) - 8 hours

**Total: ~17 hours (~2 days)**

### Sprint 2 (Week 3-4): Server Enhancements
1. ✅ Server Icon Upload (#16) - 16 hours
2. ✅ Mod Version Compatibility (#20) - 20 hours

**Total: ~36 hours (~4.5 days)**

### Sprint 3 (Week 5-6): Advanced Mod Features
1. ✅ Mod Dependency Resolution (#21) - 28 hours

**Total: ~28 hours (~3.5 days)**

### Sprint 4 (Week 7-8): Backup System
1. ✅ Automated Backups (#22) - 28 hours

**Total: ~28 hours (~3.5 days)**

### Future Sprints: Advanced Features
- Docker Architecture (#23)
- OAuth Login (#25)
- Invite System (#26)
- esheep Customization (#24)

---

## Development Guidelines

### Code Standards
- Follow existing Svelte 5 patterns (runes: $state, $derived, $effect)
- Use TypeScript strict mode
- Maintain a11y compliance (ARIA attributes)
- Write tests for new API endpoints
- Document complex logic with comments

### Git Workflow
- Create feature branch for each issue: `feature/issue-##-short-description`
- Commit frequently with descriptive messages
- Reference issue numbers in commits
- Create PR when feature is complete
- Request review before merging

### Testing Checklist
- [ ] Unit tests for new API endpoints
- [ ] Integration tests for complex features
- [ ] Manual testing on different browsers
- [ ] Test with Docker Desktop on Windows/Mac
- [ ] Test with different server types (Vanilla, Forge, Fabric, etc.)
- [ ] Load testing for performance-critical features

### Documentation
- Update README if configuration changes
- Document new API endpoints
- Add user-facing documentation for new features
- Update environment variable documentation

---

## Risk Assessment

### High Risk
- **Automated Backups (#22)**: Data corruption if not implemented carefully
  - *Mitigation:* Extensive testing, proper file locking, backup validation

- **Docker Architecture (#23)**: Breaking change for existing installations
  - *Mitigation:* Provide migration path, extensive testing, rollback plan

### Medium Risk
- **Mod Dependencies (#21)**: Complex dependency resolution logic
  - *Mitigation:* Set max dependency depth, handle edge cases carefully

- **OAuth Login (#25)**: Security implications with token storage
  - *Mitigation:* Use established libraries, encrypt tokens, security audit

### Low Risk
- Most UI features (#15, #16, #17, #18, #19) - Minimal impact on existing functionality
- Mod compatibility (#20) - Non-breaking addition

---

## Success Metrics

### Phase 1-3 Success Criteria
- All high-priority issues closed
- No new critical bugs introduced
- User satisfaction feedback positive
- Core features working reliably

### Long-term Success Criteria
- Reduced support requests for mod installation
- Increased server uptime due to backups
- Positive user feedback on UX improvements
- Active community engagement with invite system

---

## Next Steps

1. **Review this plan** with the team/stakeholders
2. **Prioritize** any adjustments based on user feedback
3. **Start with Sprint 1** (Quick Wins)
4. **Set up project board** to track progress
5. **Create feature branches** for each issue
6. **Begin implementation** following the recommended order

---

*Last Updated: 2026-01-24*
*Document Version: 1.0*
