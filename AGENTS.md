# AGENTS.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:

- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:

- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:

- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:

- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:

```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

## 5. Database PostgreSQL Conventions

Refer to DB.MD in /database
Use snake_case and singular wording.

## 6. Backend Practices

Official dotnet skills: https://github.com/dotnet/skills

## 7. Backend Testing

Backend integration tests for all critical API behavior.

## 8. Frontend Practices

For any type of complex frontend state management - use Zustand

Do **NOT** use the Supabase client for frontend - database interactions

Style design - https://styles.refero.design/

design: calltoinspiration.com

Make videos with html, css, js: https://www.heygen.com/hyperframes

Color palette: red, grey, white, black

charts for React: https://www.tradingview.com/lightweight-charts/

Prefer Temporal API for dates for frontend js

Download fonts: https://fontsource.org/

Icons: https://icones.js.org

React scan: https://github.com/aidenybai/react-scan

React to PDFs (probably don't need this yet but here for reference)" npx pdfx-cli init

## 9. Frontend Testing

Playwright for 3 to 5 high-value browser flows.

RTL for the fiddly UI states that are easy to regress.

## Suggested Agent Skills

https://github.com/obra/superpowers

npx skills add supabase/agent-skills
