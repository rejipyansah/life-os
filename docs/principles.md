# Life OS — Principles

These principles guide how Life OS is designed, developed, and evolved.

They are not feature requirements.

They are decision-making rules.

---

## 1. Life Happens First

Life OS must adapt to the user's life, not the other way around.

The system should support the way the user naturally behaves rather than forcing the user into rigid workflows.

When there is a conflict between the user's natural behavior and the application's preferred workflow, Life OS should first question the workflow.

> **Life happens first. Life OS adapts to it.**

---

## 2. Minimize Friction

Every interaction should have a reason to exist.

Users should not be required to perform multiple steps when the system can reasonably understand their intent with fewer.

Natural language should be preferred when it can make an interaction simpler.

For example:

> `Jajan 18rb cash`

should be enough to initiate a financial record.

The goal is not to eliminate every interaction.

The goal is to eliminate unnecessary ones.

> **Don't make me stop.**

---

## 3. AI Understands, Core Decides

AI should help Life OS understand the user.

It should not become the authority over critical system behavior.

AI may interpret:

* intent;
* natural language;
* context;
* ambiguous information;
* and unstructured input.

The deterministic system remains responsible for:

* validation;
* business rules;
* calculations;
* persistence;
* and decisions that require consistency.

> **AI understands. Core decides.**

---

## 4. Real Needs Before Features

Life OS should grow from real problems.

A feature should not exist simply because it is technically interesting, trendy, or possible to build.

Before adding something new, ask:

> **What real problem does this solve?**

If there is no meaningful answer, it probably does not belong in the system yet.

Life OS should be driven by needs rather than feature checklists.

---

## 5. Low Maintenance

Life OS should not become another responsibility.

The system should minimize the amount of maintenance required from its user.

Users should not constantly need to:

* reorganize information;
* configure complicated systems;
* clean up unnecessary data;
* maintain elaborate workflows;
* or remember how the application expects them to behave.

If the system requires too much maintenance to remain useful, the system is failing its purpose.

> **The system should carry its share of the work.**

---

## 6. Available, Not Intrusive

Life OS should be available whenever the user needs it.

But availability does not mean constant activity.

Life OS should not constantly:

* interrupt;
* remind;
* notify;
* recommend;
* or demand attention.

It should know when to stay quiet.

Proactive behavior should exist when it provides meaningful value, not simply because the technology makes it possible.

> **A companion is present without being intrusive.**

---

## 7. One System, Multiple Domains

Life OS may contain many modules, but it should remain one coherent system.

Finance, Health, Productivity, Knowledge, and future domains should not become isolated mini-applications.

They should have clear boundaries while being capable of participating in a shared understanding of the user's life.

The user should not have to think:

> "Which application does this belong to?"

The system should handle that complexity where possible.

---

## 8. Modules Own Their Domain

While Life OS should behave as one system, each module must remain responsible for its own domain.

Finance owns financial rules.

Health owns health-related domain logic.

Productivity owns productivity-related logic.

Modules should not depend unnecessarily on the internal implementation of other modules.

This keeps Life OS capable of growing without becoming a tangled collection of dependencies.

> **Connected does not mean coupled.**

---

## 9. Data Is the Source of Truth

Life OS should distinguish between:

* what the user said;
* what AI inferred;
* and what the system knows to be true.

AI interpretation is not automatically fact.

For example:

```text
User:
"Jajan 18rb cash"
        ↓
AI interpretation:
CreateExpense
Amount = 18000
Account = Cash
        ↓
System validation
        ↓
Stored financial transaction
```

The stored domain data becomes the source of truth.

This distinction becomes increasingly important as Life OS grows.

---

## 10. Privacy by Default

Life OS may contain deeply personal information.

Privacy should therefore be considered part of the architecture, not an optional feature added later.

Private information should remain isolated from:

* public demonstrations;
* development data;
* testing environments;
* and portfolio presentations.

The system should be designed with the assumption that personal data deserves protection.

> **Personal first. Public second.**

---

## 11. Evolution Over Completeness

Life OS does not need to solve everything immediately.

A module should become useful before the next module is introduced.

The system should evolve through real usage:

```text
Real problem
    ↓
Small solution
    ↓
Real usage
    ↓
Learning
    ↓
Improvement
    ↓
New problem
    ↓
Evolution
```

Completeness is not the goal.

Continuous usefulness is.

> **Build what life needs next.**

---

## 12. Boring Technology Is Allowed

Life OS should use technology that serves the system rather than technology that exists to make the system look impressive.

New technology should have a meaningful reason to exist.

We should prefer:

* simplicity;
* maintainability;
* reliability;
* clarity;
* and appropriate complexity.

A boring solution that works well is better than an impressive solution that creates unnecessary problems.

> **Engineering serves the vision. Not the other way around.**
