# Changelog

## 0.2.2

- Support crops whose produce is also their planting material, including Lemons
  and Cherries, with identity checks before and after replanting.
- Transfer only surviving newly harvested items to venue inventory.
- Keep committed delivery receipts available if farm sourcing pauses after a failure.
- Add shared-type harvest and ownership regression checks (199 total checks).

## 0.2.1

- Log observed harvest counts and attempt one verified same-crop replant.
- Isolate recoverable unit failures while allowing native vendor purchasing.

## 0.2.0

- Fill venue storage with the whole harvest; retain only capacity overflow in
  player inventory.
- Add zero-cost farm-supply rows without changing native finance totals.
- Store optional receipt history in sidecars bound to exact save bytes.
- Attempt API-checked loading after updates, with native gameplay fallback.

## 0.1.1

- Read the keyed inventory stack index with ownership and version checks,
  avoiding the failing boxed IL2CPP linked-list enumerator.

## 0.1.0

- Initial farm-first manager supply and automatic same-crop replanting.
