# VisionPaint UI/UX Design

**Source of truth for UI work.** Agents and implementers should read these before building frontend screens.

Color palette: red, grey, white, black (semantic status badges: see [ui-spec.md](./ui-spec.md#brand-palette-vs-semantic-colors))

| Document | Purpose |
|----------|---------|
| [ui-spec.md](./ui-spec.md) | Design tokens, layout rules, per-screen behavior, states, API hooks |
| [screen-map.md](./screen-map.md) | Screen inventory, routes, roles, MVP priority |
| [user-journeys.md](./user-journeys.md) | End-to-end workflows (J1–J6) |
| [stakeholder-decisions.md](./stakeholder-decisions.md) | Locked product decisions after review |
| [stakeholder-one-pager.md](./stakeholder-one-pager.md) | Printable summary for demos |
| [components.md](./components.md) | Vue component inventory + Pinia stores |
| [wireframes/](./wireframes/) | Static HTML + Tailwind prototypes |

## Visual inspiration (reference only)

- [Refero](https://styles.refero.design/) — layout and component patterns
- [Call to Inspiration](https://calltoinspiration.com/) — mobile UI ideas

Do not treat external sites as spec; link chosen patterns in `ui-spec.md` when adopted.

## Optional

- Excalidraw files may be added under `wireframes/*.excalidraw` for stakeholder walkthroughs. Markdown spec remains canonical.

## Checklist

- [x] UI spec + screen map + journeys
- [x] Stakeholder review (decisions recorded)
- [x] Wireframes for all P0 screens
- [x] Vue component inventory
- [ ] Implement Vue shell + routes (Week 7+)
- [ ] Align Tailwind v4 theme with design tokens in app
