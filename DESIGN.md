# Billing Control Clean-room Design System

## Direction: Reconciliation Desk

Billing Control is an internal accounting control room. The interface should feel like a quiet working surface for reconciling records, following exceptions, and moving the next item forward.

The visual direction is precise, calm, practical, and restrained:

- cool paper background with clean white working sheets;
- navy-ink typography and graphite rules for structure;
- ledger orange used sparingly for attention and the primary action;
- compact, aligned rows instead of a wall of equal-weight cards;
- state communicated by text, labels, and restrained color, never by decoration alone.

## First viewport contract

The first viewport answers three questions in order:

1. What is this workspace and what should I do next?
2. Which records need attention now?
3. Where can I continue the current accounting work?

The Dashboard uses a fixed navigation rail, a command header, one prominent needs-attention register, and compact working sheets. Financial summaries remain available through an intentional disclosure instead of competing with the action register.

## Signature interaction

The signature interaction is the attention register: a vertically aligned list of actionable exceptions with a clear count, concise reason, and direct route. It behaves like a reconciliation queue, not a collection of promotional cards.

## Motion grammar

- 140ms for controls, navigation states, and disclosure transitions.
- 180ms for the sidebar drawer and dialog surfaces.
- ease-out timing, no spring or bounce.
- motion is supplementary; focus, hover, and open states remain clear without animation.
- `prefers-reduced-motion: reduce` removes transitions and transforms.

## Palette

| Token | Value | Use |
| --- | --- | --- |
| Ink 950 | `#152331` | headings, navigation, primary text |
| Ink 700 | `#3F5262` | supporting text, table labels |
| Ink 500 | `#71818C` | muted text, metadata |
| Paper | `#EFF2F1` | page background |
| Sheet | `#FBFCFA` | working surfaces, forms, tables |
| Line | `#CFD8D7` | borders, dividers, grid rules |
| Copper | `#C66A3C` | primary action, attention signal |
| Copper deep | `#9D4E2A` | hover and active copper states |
| Copper wash | `#F8ECE5` | selected or attention surface |
| Sage | `#547866` | positive or complete state |
| Amber | `#A87524` | pending or due state |
| Rose | `#A84F56` | overdue or blocked state |
| Blue | `#416985` | informational state |

No gradients, glass effects, neon colors, or decorative illustration are part of the system.

## Typography

The preferred sans-serif is IBM Plex Sans when available, with Segoe UI and the system sans-serif stack as dependable fallbacks. Numerals in financial and operational tables use tabular figures.

- Page title: 30–38px, 650 weight, tight line-height.
- Section title: 16px, 650 weight.
- Body and controls: 14px, 400–600 weight.
- Table and metadata labels: 11–12px, 600 weight, modest tracking.
- Supporting copy: 13px, 400 weight, maximum measure around 62ch.

Titles are direct. Avoid marketing-style kicker lines, decorative eyebrow labels, and oversized display typography.

## Layout and spacing

The base spacing scale is 4, 8, 12, 16, 24, and 32px. The desktop navigation rail is 232px wide. The content surface is capped at 1440px and uses 24–40px outer padding depending on viewport width.

At 800–1000px split-screen widths, the page keeps a useful desktop density:

- the navigation becomes a controlled drawer rather than forcing a horizontal page layout;
- the page header keeps the primary action visible;
- the attention register remains a single scan-friendly column;
- working sheets may share a two-column lane when their content can stay readable;
- tables scroll inside their own surface when columns need more room;
- the document itself never gains horizontal overflow.

## Component standards

### Navigation rail

The rail groups routes into the five operating areas defined in `PRODUCT.md`. Each item has a short text label, a CSS geometry marker, and an active state with a copper rule and tonal wash. Role visibility follows the existing authorization contract; it is not an authorization boundary.

### Page header

The header contains the page title, one useful sentence of context, and a small command group. The primary action is a solid copper button. Secondary actions are quiet outline or text controls. Avoid multiple competing primary buttons.

### Attention register

Rows use a consistent three-part arrangement: state mark, reason, and next route. The count is explicit. Empty state text explains that there are no current exceptions instead of showing a zero-value metric card.

### Working sheet

Sheets are white surfaces with a single border, a compact header, and a clear content purpose. A sheet may contain a table, a form, or a short operational list. Do not turn every metric or section into a separate floating card.

### Buttons and controls

Controls have a 7px radius, 40px minimum height, clear focus rings, and sentence-case labels. The primary copper action is reserved for the next meaningful operation. Destructive actions are explicit and never styled as ordinary secondary actions.

### Forms and filters

Filters use a compact grouped form with visible labels, native selects, date inputs, and a clear reset path. Filters are disclosed when they are not the immediate task, and their applied state is described in text. Field errors and required state use text and `aria-describedby`, not color alone.

### Data grids

Tables use aligned columns, a calm header row, tabular numerals, and internal horizontal scrolling where necessary. Status is textual and badge-assisted. The grid wrapper owns overflow so the page remains usable at split-screen widths.

### Status badges

Badges are compact labels, not decorative pills. They use a restrained radius and a text label such as `Active`, `Pending`, `Overdue`, or `Paid`. Color supplements the label and never carries the meaning alone.

### Modal and dialog standards

Dialogs use a solid sheet surface, a clear heading, a concise description, and an obvious close action. The backdrop is quiet. Focus and Escape handling are provided by the shell script, while form submission remains owned by the existing backend contracts.

## Non-goals

Phase 1 does not redesign every route. It does not change business logic, calculations, database schema, permissions, routes, controllers, models, accounting behavior, or workflow contracts. It does not introduce mobile-first layouts, decorative illustration, dashboard gamification, or a new client-side application framework.
