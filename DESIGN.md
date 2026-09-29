---
name: Billing Control
description: A calm, role-scoped accounting operations workspace for billing, work, and financial records.
colors:
  orange-primary: "#ff6115"
  orange-strong: "#e94f08"
  orange-soft: "#fff0e7"
  orange-wash: "#fff8f3"
  navy-ink: "#102545"
  slate-ink: "#40536e"
  muted-slate: "#73839a"
  cool-shell: "#f4f6f8"
  cream-canvas: "#fffcf4"
  white-surface: "#ffffff"
  surface-muted: "#fffaf5"
  line: "#e9e7e1"
  line-strong: "#ddd8ce"
  panel-line: "#e5e8ec"
  success: "#159669"
  success-soft: "#e8f8f0"
  info: "#3978df"
  info-soft: "#edf4ff"
  warning: "#c77810"
  warning-soft: "#fff4dd"
  purple: "#8157db"
  purple-soft: "#f2edff"
  danger: "#dc4d59"
  danger-soft: "#fff0f1"
  nav-neutral: "#f4f4f5"
  nav-neutral-hover: "#f7f7f8"
  nav-accent-soft: "#fff1e8"
typography:
  display:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(27px, 2.1vw, 35px)"
    fontWeight: 750
    lineHeight: 1.1
    letterSpacing: "-1.15px"
  headline:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "17px"
    fontWeight: 750
    lineHeight: 1.25
    letterSpacing: "-0.35px"
  title:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "15px"
    fontWeight: 750
  body:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "9px"
    fontWeight: 800
    lineHeight: 1
    letterSpacing: "1.65px"
rounded:
  sm: "8px"
  md: "12px"
  lg: "16px"
  pill: "999px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  2xl: "24px"
  page: "32px"
  wide: "42px"
components:
  button-primary:
    backgroundColor: "{colors.orange-primary}"
    textColor: "{colors.white-surface}"
    rounded: "{rounded.sm}"
    padding: "8px 14px"
    height: "38px"
  button-secondary:
    backgroundColor: "{colors.white-surface}"
    textColor: "{colors.slate-ink}"
    rounded: "{rounded.sm}"
    padding: "8px 14px"
    height: "38px"
  button-quiet:
    backgroundColor: "transparent"
    textColor: "{colors.muted-slate}"
    rounded: "{rounded.sm}"
    padding: "8px 14px"
    height: "38px"
  input-default:
    backgroundColor: "{colors.white-surface}"
    textColor: "{colors.navy-ink}"
    rounded: "{rounded.sm}"
    padding: "8px 11px"
    height: "39px"
  panel:
    backgroundColor: "{colors.white-surface}"
    rounded: "{rounded.md}"
    padding: "20px"
  status-chip:
    backgroundColor: "{colors.success-soft}"
    textColor: "{colors.success}"
    rounded: "{rounded.pill}"
    padding: "4px 8px"
  record-link:
    backgroundColor: "#f2f5f9"
    textColor: "#35577f"
    rounded: "6px"
    padding: "4px 7px"
---

# Design System: Billing Control

## Overview

**Creative North Star: "Accounting Control Room"**

Billing Control is a calm operational interface for people who need to read financial state, move work forward, and make careful corrections. Its visual language is precise, practical, and professional: cool paper surrounds white working surfaces, typography carries hierarchy, and a small orange signal marks action, attention, and current location.

The system is layered rather than decorative. Thin neutral borders establish structure first; restrained ambient shadows and tonal changes provide just enough depth for panels, dialogs, drawers, and interactive metrics. Dense registers remain readable through sticky headers, tabular numbers, deliberate column widths, and predictable inner scrolling.

**Key Characteristics:**
- Cool gray application shell with white work surfaces.
- Ledger Orange reserved for primary actions, active navigation, focus, and selected states.
- Compact, evidence-led information density for registers and financial data.
- Calm status semantics through restrained green, blue, amber, purple, and rose chips.
- Responsive single-panel navigation and viewport-owned register/dashboard scrolling.

## Colors

The palette is Ledger Orange + Navy Ink + Cool Paper: a warm operational signal inside a restrained, neutral workspace.

### Primary
- **Ledger Orange:** the decisive action color for primary buttons, active rails, focus rings, progress accents, and attention-worthy controls.
- **Ember Orange:** the stronger interaction state for hover, active, and emphasized action text.
- **Orange Soft / Orange Wash:** quiet selection and hover surfaces; use them to support orange without turning the whole screen into a brand field.

### Tertiary
- **Success Green:** completed, paid, submitted, and recorded states.
- **Information Blue:** upcoming and informational states.
- **Warning Amber:** work in progress, review, and attention states.
- **Review Purple:** final management-report states and exceptional review milestones.
- **Danger Rose:** missing, late, cancelled, blocked, disabled, and validation-error states.

### Neutral
- **Navy Ink:** high-contrast headings, key values, and primary content.
- **Slate Ink:** supporting content, labels, and secondary controls.
- **Muted Slate:** helper copy, metadata, captions, and low-priority context.
- **Cool Shell:** the current application canvas behind working surfaces.
- **Cream Canvas:** the warmer login canvas and legacy warm surface context.
- **White Surface:** panels, fields, topbar, sidebar, and primary working areas.
- **Surface Muted:** warm supporting strips, table hover surfaces, and quiet summaries.
- **Mist Lines:** thin borders and separators that define structure without visual noise.

### Named Rules
**The Signal Rarity Rule.** Orange marks action, location, focus, and attention; it is not a decorative background color.

**The State Semantics Rule.** Status hues communicate workflow meaning. Do not repurpose success, warning, danger, or review colors as ornament.

## Typography

**Display Font:** Inter, with ui-sans-serif and system UI fallbacks.
**Body Font:** Inter, with ui-sans-serif and system UI fallbacks.
**Label/Mono Font:** No separate label or mono family; numeric values use tabular figures where financial comparison matters.

**Character:** The system is crisp and workmanlike. Weight, scale, spacing, and tabular numerals provide hierarchy without relying on decorative type or an expressive display face.

### Hierarchy
- **Display** (750, responsive 27–35px, 1.1): page titles and major workspace headings.
- **Headline** (750, 17px, 1.25): panel titles and section headings.
- **Title** (750, 15px): compact subheadings and small content titles.
- **Body** (400, 14px, 1.5): default application copy and operational descriptions.
- **Label** (800, 9px, 1 line-height, wide tracking): uppercase page eyebrows and compact category signals.

### Named Rules
**The Operational Hierarchy Rule.** Let short labels, strong headings, quiet helper copy, and tabular figures make dense data scannable; do not solve hierarchy with decorative typography.

## Layout

The application uses a two-region desktop shell: a fixed single-panel sidebar and a flexible working area. The final desktop sidebar is 224px wide; the app shell occupies the remaining width, while content is centered within a maximum width of 1680px. The topbar is sticky at 64px high, and the main content uses generous horizontal padding that scales from 20px to 42px.

Navigation is grouped with native `details`/`summary` disclosure. The active child link uses a quiet neutral fill and a narrow orange leading rail. At widths up to 900px, the sidebar becomes a drawer no wider than 224px with a scrim; the application shell becomes full width. At 680px, headings and actions stack, forms become one column, filters become a single-column grid, and wide registers keep their data integrity through horizontal scrolling. At 420px, metric cards stack to one column.

The dashboard uses a two-column working grid on desktop and a single column below 900px. Metric strips use responsive equal-width columns. Register and dashboard pages own the viewport height, keeping headings and controls stable while only the content region scrolls; data-grid headers remain sticky. This is a deliberate operational behavior, not an incidental overflow fix.

### Named Rules
**The Stable Frame Rule.** Keep navigation, page context, filters, and register controls stable while the data region scrolls.

**The Honest Density Rule.** Preserve readable column widths and scroll wide financial tables rather than compressing or hiding meaning to fit a narrow viewport.

## Elevation & Depth

Depth is layered and restrained. Cool shell color separates the application frame from white working surfaces; one-pixel borders establish panel boundaries; subtle shadows add ambient lift to cards, metrics, and overlays. Hovering a metric or checklist family may raise it slightly with a stronger ambient shadow. The topbar uses a translucent white surface with a light blur, while filter dialogs and the mobile drawer receive stronger separation from the page behind them.

### Shadow Vocabulary
- **Quiet card shadow** (`0 2px 8px rgb(38 32 22 / 3%)`): default low-contrast separation for panels and fields.
- **Elevated interaction shadow** (`0 12px 34px rgb(28 39 55 / 7%)`): hover state for metric cards and checklist families.
- **Topbar separation** (`0 1px 0 rgb(24 30 38 / 2%)`): barely visible boundary beneath the sticky header.
- **Dialog and drawer shadow**: stronger separation reserved for filter dialogs and mobile navigation overlays.

### Named Rules
**The Layered-Not-Lifted Rule.** Use tonal surfaces and hairline borders first; use shadow as ambient support, never as the primary visual language.

## Shapes

The form language is gently rounded and compact. Small controls, buttons, inputs, navigation links, and warnings use 8px corners; panels and metric cards use 12px corners; login and prominent outer surfaces use 16px corners. Status badges and live indicators are pill-shaped. Borders are generally one pixel and quiet, with orange appearing only for action, focus, or current-location emphasis.

## Components

### Buttons
- **Shape:** Compact, gently rounded controls (8px radius), with a minimum height of 38px; small buttons reduce to 30px.
- **Primary:** Ledger Orange fill with white text, medium horizontal padding, and no default shadow; use for the main action in a page or form.
- **Secondary:** White fill with a soft neutral border and Slate Ink text; hover moves to a quiet orange-wash surface.
- **Quiet:** Transparent and low-emphasis for reset or secondary navigation actions; hover gains a neutral border and pale gray surface.
- **Hover / Focus:** A small upward hover shift is allowed for buttons; focus-visible uses a clearly visible orange outline with offset.

### Status Chips
- **Style:** Compact, left-aligned pills with 4px vertical and 8px horizontal padding; backgrounds are pale semantic tints with darker semantic text.
- **State:** Use success, information, warning, review, danger, and muted mappings consistently. Never rely on hue alone where adjacent text can name the state.

### Cards / Containers
- **Corner Style:** 12px for panels and metrics; 16px for the login card.
- **Background:** White working surfaces on the Cool Shell; warm muted strips are reserved for summaries, table headers, and hover states.
- **Shadow Strategy:** Quiet card separation at rest; stronger ambient elevation only for hover, dialogs, and drawers.
- **Border:** One-pixel neutral borders; active or hovered navigation uses a more visible state treatment.
- **Internal Padding:** Panel headers typically use 17–20px; mobile panels reduce toward 14–18px.

### Inputs / Fields
- **Style:** White fields with 8px corners, compact 8px/11px padding, and a 39px minimum height. Labels are small and Slate Ink.
- **Focus:** Orange border shift with a soft orange focus ring; focus-visible remains clear for keyboard users.
- **Error / Disabled:** Rose-tinted error surfaces and text; readonly fields use a muted gray background and text rather than looking interactive.
- **Searchable Select:** Floating white option menu with a quiet border, compact rows, and orange-wash hover/active state.

### Navigation
- **Style:** A fixed 224px white sidebar with grouped disclosure sections, simple symbol icons, and compact text labels.
- **Default:** Neutral text on white; open group summaries use a pale orange surface and Ember Orange text.
- **Active:** The child link uses a soft neutral fill, dark text, stronger weight, and a narrow Ledger Orange leading rail.
- **Mobile:** The same panel becomes a drawer with a scrim at 900px and below; Escape and the scrim close it.

### Data Registers
- **Style:** White panels with sticky light-gray headers, compact 11.5px table text, 48px default body rows, and horizontal scrolling for wide data.
- **Interaction:** Register controls add stable sorting, multi-column Shift-click sorting, column visibility, row limits, text/date/amount filters, and empty-state messaging on the client side.
- **Financial Values:** Right-align money, preserve tabular numerals, and retain explicit RM context in headings or values.

### Metrics
- **Style:** Responsive cards with a 3px semantic top rule, a strong tabular value, a quiet label, and optional explanatory copy.
- **Interaction:** Hover can raise a metric by 2px with the elevated interaction shadow; the reduced-motion preference removes transition movement.

## Do's and Don'ts

### Do:
- **Do** keep Ledger Orange rare and purposeful: primary action, active rail, focus ring, and attention signal.
- **Do** use Navy Ink for decisions and Slate/Mute for supporting context.
- **Do** keep registers scannable with sticky headers, stable widths, right-aligned money, and inner scrolling.
- **Do** preserve the 8px / 12px / 16px radius hierarchy and compact control heights.
- **Do** make focus-visible states obvious and preserve the existing keyboard-friendly disclosure, dialog, and drawer behavior.
- **Do** reduce density gracefully at 900px, 680px, and 420px rather than introducing a second visual language.
- **Do** honor reduced-motion preferences for transitions and scrolling.

### Don't:
- **Don't** introduce playful illustrations, flashy gradients, futuristic effects, decorative patterns, or overly corporate visual ceremony.
- **Don't** add new font families or expressive type treatments without an explicit identity decision.
- **Don't** use orange as a large-area background or treat semantic status colors as decoration.
- **Don't** compress financial tables until labels or values become ambiguous; scroll them honestly instead.
- **Don't** replace named workflow/status text with color-only indicators.
- **Don't** remove the fixed-frame and inner-scroll behavior that keeps registers usable during long sessions.
