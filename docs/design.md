# Life OS — Design Direction

A practical design direction for Life OS frontend work.

This is not a design system. It captures decisions and intent for building a consistent, personal interface across modules.

---

## What Life OS Is

Life OS is a personal workspace.

Not a fintech dashboard. Not a SaaS admin panel. Not a project management tool.

It is a personal operating system that lives alongside its user.

The interface should feel like a tool the user opens to do something, not a dashboard they manage.

**What Life OS is not:**

Life OS should never feel like a generic SaaS product, an enterprise dashboard, a wellness app, a fintech template, or an overly decorative marketing UI. These patterns erode the personal, calm character that defines the product.

---

## Visual Principles

**Personal, calm, intentional.**

Life OS should feel private and considered. It should not shout.

**Atmosphere is part of the experience.**

Life OS should feel like a personal space users want to return to, not merely a functional data-entry application. Subtle gradients, soft color transitions, atmospheric backgrounds, and layered surfaces are allowed when they strengthen the feeling of Life OS. Visual atmosphere is part of the product experience.

**Gradients are allowed, but must remain restrained.**

Gradients may support atmosphere and depth without hurting readability, hierarchy, contrast, or usability. They should never compete with content or become the focal point.

**Functional over decorative.**

Every visual element should serve a purpose. If it does not help the user understand or act, it does not belong. Functional clarity, accessibility, hierarchy, and usability remain more important than decoration.

**Information hierarchy over excessive containers.**

Use typography, spacing, and subtle weight to create hierarchy. Do not default to wrapping everything in cards or bordered boxes.

**Cards are conditional.**

Cards are acceptable when they provide real grouping value. They are not the default layout strategy. Prefer subtle separators and surface variation over heavy card grids.

**Avoid visual noise.**

No glassmorphism. No excessive shadows. No excessive rounded cards. No decorative illustrations. No unnecessary charts. Avoid excessive icon usage. Visual noise is anything that competes with the user's focus without adding meaning.

---

## Implementation Technology

**Tailwind CSS is approved.**

Tailwind CSS may be used to translate and maintain the Life OS design system. It is a tooling choice, not the source of design decisions.

**Life OS visual language is the source of truth.**

Design decisions originate from the principles in this document, not from Tailwind's defaults or utility patterns. Tailwind serves the design, not the other way around.

**Stitch prototypes as visual references.**

Stitch prototypes may be used as visual references when they successfully express the intended Life OS character. They are references, not specifications. Implementation decisions should still follow the principles above.

---

## Interaction Principles

**Natural input is the primary interaction pattern.**

When applicable, the user should be able to express intent directly rather than navigate structured workflows. Forms and manual input remain available as fallback.

**Frequent actions stay close.**

Actions the user performs often should be within easy reach, especially on mobile. Thumb reach and scroll behavior matter more than theoretical responsive breakpoints.

**Reduce friction, not just steps.**

Life OS should adapt to how the device is actually used. Mobile sticky interactions are acceptable when they improve frequent access. Desktop should use available space intentionally.

---

## Responsive Behavior

**Mobile-first.**

Design for the phone first. Desktop should not simply be a narrow mobile page centered on a large screen.

**Intentional use of space.**

Desktop layouts should feel deliberate, not cramped or wasteful.

**Device-aware, not just screen-size-aware.**

Responsive behavior should adapt to how the device is actually used, not just to CSS breakpoints.

---

## Semantic Use of Color

Color should communicate meaning when useful.

For example: positive and negative financial movement, status indicators, or domain-specific signals.

Color should not exist purely for decoration.

Primary content must remain readable and accessible. Secondary metadata can be visually quieter but must remain comfortably readable on mobile.

---

## Content Philosophy

**Concise and useful.**

Prefer short labels and actionable information over promotional or decorative copy.

**Quiet by default.**

Avoid unnecessary confirmations, celebrations, and visual noise. A successful transaction does not need a banner. Confidence is enough.

**Not a dashboard.**

The interface should feel like a personal tool, not a management console.

---

## Cross-Module Consistency

Future modules — Health, Productivity, Knowledge, Journal, Goals — should share the same visual language.

Individual modules may use domain-specific semantic styling, but they should feel like parts of one Life OS.

The user should not experience moving between unrelated applications when switching modules.

---

## Home / Beranda

**Home is context, not a dashboard.**

Home surfaces what is meaningful now. It is not a screen that exposes every available metric or module. Home provides context and direction; modules provide deeper interaction and detail.

**State-driven sections.**

Sections appear based on meaningful state. The interface does not preserve empty sections to maintain a fixed layout. No active target means no progress section. No relevant current items means no "Penting Sekarang" section.

**Evidence over motivation.**

Progress is shown through concrete evidence rather than motivational language, gamification, or artificial encouragement. Show the evidence and let the user interpret its meaning.

**Same information across responsive compositions.**

Desktop and mobile may use different layouts, density, and interaction presentation. They must preserve the same information, data, state, semantic meaning, and available actions. Responsive design may change presentation but must not remove a capability because the user changed device.

**Desktop and mobile composition.**

Desktop uses an asymmetric two-column editorial grid (7/5 split). Mobile uses a single stacked column. Navigation adapts: bottom tab bar on mobile, top navigation bar on desktop. The same destinations and same enabled/disabled states apply on both.

**Quiet visual direction.**

Home follows the same visual language as the rest of Life OS: calm atmosphere, organic surfaces, restrained elevation, hairline borders. No glassmorphism, no unnecessary cards, no decorative charts, no excessive icon usage.

---

## What This Document Is Not

- Not a full color palette or typography scale.
- Not a spacing token system or component library.
- Not a final visual specification.

This is a direction document. It captures how Life OS should feel and behave, not exact pixel values or hex codes.

Design decisions that fall below this level of detail should be made in implementation, guided by this direction and the existing principles.
