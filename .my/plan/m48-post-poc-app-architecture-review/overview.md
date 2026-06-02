# M48 - Post-PoC App Architecture Review

**Status:** Report created

## Goal

Review BodyCam's current app architecture now that it is moving beyond proof of
concept.

The main lens for this report is:

- BodyCam is still an assistive app for blind and visually impaired users.
- AI models, cameras, microphones, speakers, buttons, and connected devices are
  supporting pieces, not the product identity by themselves.
- The architecture should stay plug-and-play where that helps.
- The architecture should use good-enough OOP.
- The architecture should stay simple and avoid overengineering.

## Result

See [Architecture Review And Improvement Proposal](./report.md).
See also [Phase Map](./phase-map.md) for implementation sequencing and command
naming decisions.

Short answer:

- the app already has good registry/provider seams in several places;
- the biggest issue is not lack of abstraction, but too many runtime owners and
  too much startup/policy logic spread across UI, managers, and device-specific
  services;
- the next step should be consolidation, not reinvention.

## Follow-Up Direction

The report recommends a product-phase architecture built around:

- one runtime bootstrap owner;
- one source-selection owner;
- simple registry-based providers;
- assistive workflows organized around user intent;
- split settings stores instead of one giant settings surface.

Recommended next implementation milestone:

- unify runtime ownership and source selection first;
- then consolidate settings and workflow entry points.
