#!/usr/bin/env python3
"""
Structural repairs on Unity scene/prefab YAML, for when the editor is not available.

This project's frontend scene was written by a code path that built UI under a
stale `uiRoot` reference. Unity treats a destroyed Transform as null, so
`SetParent(uiRoot)` silently placed every panel at the *scene root* instead of
under the Canvas. uGUI draws nothing outside a Canvas, and it does not warn, so
the menu simply vanished with no error to chase.

The fix is a hierarchy change, which is a two-minute drag in the editor and a
careful edit by hand: a parent change touches three places (the child's
`m_Father`, the parent's `m_Children`, and the scene's `m_Roots` list) and
missing any one of them corrupts the scene.

Scope note: this deliberately does *not* try to be a general Unity YAML editor.
It performs named, idempotent repairs whose correctness can be argued about, and
refuses anything ambiguous rather than guessing.

    ./Tools/unity-scene-repair.py list-roots Assets/Scenes/StartMenu.unity
    ./Tools/unity-scene-repair.py adopt-orphan-ui Assets/Scenes/StartMenu.unity --dry-run
    ./Tools/unity-scene-repair.py adopt-orphan-ui Assets/Scenes/StartMenu.unity \
        --order Panel_PLAY,Panel_OPERATORS,BottomBar,TopBar
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

TRANSFORM = 4
RECT_TRANSFORM = 224
GAME_OBJECT = 1
CANVAS = 223
SCENE_ROOTS = 1660057539

DOC_RE = re.compile(r"^--- !u!(\d+) &(\d+)(.*)$", re.M)


@dataclass
class Document:
    class_id: int
    file_id: int
    start: int
    end: int
    text: str = ""


@dataclass
class Scene:
    path: Path
    text: str
    docs: list[Document] = field(default_factory=list)

    @classmethod
    def load(cls, path: Path) -> "Scene":
        text = path.read_text()
        scene = cls(path=path, text=text)
        matches = list(DOC_RE.finditer(text))
        for i, m in enumerate(matches):
            end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
            scene.docs.append(
                Document(int(m.group(1)), int(m.group(2)), m.start(), end, text[m.start():end])
            )
        return scene

    def by_id(self, file_id: int) -> Document | None:
        for d in self.docs:
            if d.file_id == file_id:
                return d
        return None

    def of_class(self, class_id: int) -> list[Document]:
        return [d for d in self.docs if d.class_id == class_id]

    def rebuild(self) -> None:
        """Re-emit the file from the (possibly edited) document texts."""
        head = self.text[: self.docs[0].start] if self.docs else self.text
        self.text = head + "".join(d.text for d in self.docs)
        # Recompute offsets so a second pass sees consistent state.
        pos = len(head)
        for d in self.docs:
            d.start, d.end = pos, pos + len(d.text)
            pos = d.end

    def save(self) -> None:
        self.rebuild()
        self.path.write_text(self.text)


# --------------------------------------------------------------------------- #
# Field access
# --------------------------------------------------------------------------- #


def get_field(doc: Document, key: str) -> str | None:
    m = re.search(rf"^\s*{re.escape(key)}:[ \t]*(.*)$", doc.text, re.M)
    return m.group(1).strip() if m else None


def set_field(doc: Document, key: str, value: str) -> bool:
    new, n = re.subn(
        rf"^(\s*){re.escape(key)}:[ \t]*.*$", rf"\g<1>{key}: {value}", doc.text, count=1, flags=re.M
    )
    if n:
        doc.text = new
    return bool(n)


def ref_id(value: str | None) -> int:
    if not value:
        return 0
    m = re.search(r"fileID:\s*(-?\d+)", value)
    return int(m.group(1)) if m else 0


def get_list(doc: Document, key: str) -> list[int]:
    """Read a YAML block sequence of `- {fileID: N}` under `key`."""
    m = re.search(rf"^\s*{re.escape(key)}:[ \t]*(\[\])?[ \t]*$\n?((?:\s*- .*\n)*)", doc.text, re.M)
    if not m:
        return []
    return [int(x) for x in re.findall(r"fileID:\s*(-?\d+)", m.group(2) or "")]


def set_list(doc: Document, key: str, ids: list[int], indent: str = "  ") -> bool:
    """Replace a block sequence, collapsing to `[]` when empty, as Unity does."""
    pattern = re.compile(rf"^(\s*){re.escape(key)}:[ \t]*(\[\])?[ \t]*$\n?((?:\s*- .*\n)*)", re.M)
    m = pattern.search(doc.text)
    if not m:
        return False
    pad = m.group(1)
    if ids:
        body = f"{pad}{key}:\n" + "".join(f"{pad}- {{fileID: {i}}}\n" for i in ids)
    else:
        body = f"{pad}{key}: []\n"
    doc.text = doc.text[: m.start()] + body + doc.text[m.end():]
    return True


# --------------------------------------------------------------------------- #
# Scene queries
# --------------------------------------------------------------------------- #


def name_of_transform(scene: Scene, transform_id: int) -> str:
    t = scene.by_id(transform_id)
    if not t:
        return f"<missing {transform_id}>"
    go = scene.by_id(ref_id(get_field(t, "m_GameObject")))
    return (get_field(go, "m_Name") or "<unnamed>") if go else "<no gameobject>"


def components_of(scene: Scene, game_object_id: int) -> list[Document]:
    go = scene.by_id(game_object_id)
    if not go:
        return []
    ids = [int(x) for x in re.findall(r"component:\s*\{fileID:\s*(\d+)\}", go.text)]
    return [d for d in (scene.by_id(i) for i in ids) if d]


def scene_roots_doc(scene: Scene) -> Document:
    docs = scene.of_class(SCENE_ROOTS)
    if len(docs) != 1:
        sys.exit(f"expected exactly one SceneRoots document, found {len(docs)}")
    return docs[0]


# --------------------------------------------------------------------------- #
# Commands
# --------------------------------------------------------------------------- #


def cmd_list_roots(scene: Scene, _args: argparse.Namespace) -> int:
    roots = get_list(scene_roots_doc(scene), "m_Roots")
    print(f"{scene.path}: {len(roots)} root object(s)")
    for r in roots:
        doc = scene.by_id(r)
        kind = {TRANSFORM: "Transform", RECT_TRANSFORM: "RectTransform"}.get(
            doc.class_id if doc else 0, f"!u!{doc.class_id}" if doc else "MISSING"
        )
        children = len(get_list(doc, "m_Children")) if doc else 0
        print(f"  {r:>12}  {kind:<14} {name_of_transform(scene, r):<20} {children} child(ren)")
    return 0


def cmd_adopt_orphan_ui(scene: Scene, args: argparse.Namespace) -> int:
    """Move root-level RectTransforms under the scene's Canvas."""
    roots_doc = scene_roots_doc(scene)
    roots = get_list(roots_doc, "m_Roots")

    # Every Canvas in the scene, as (rect transform id, name).
    canvases: list[tuple[int, str]] = []
    for canvas in scene.of_class(CANVAS):
        go_id = ref_id(get_field(canvas, "m_GameObject"))
        for comp in components_of(scene, go_id):
            if comp.class_id == RECT_TRANSFORM:
                canvases.append((comp.file_id, name_of_transform(scene, comp.file_id)))

    if not canvases:
        sys.exit("no Canvas in this scene — nothing can adopt the orphaned UI")
    if args.canvas:
        canvases = [c for c in canvases if c[1] == args.canvas]
        if not canvases:
            sys.exit(f"no Canvas named {args.canvas!r}")
    if len(canvases) > 1:
        names = ", ".join(n for _, n in canvases)
        sys.exit(f"{len(canvases)} canvases ({names}) — pass --canvas to choose one")

    canvas_rt, canvas_name = canvases[0]
    canvas_doc = scene.by_id(canvas_rt)
    assert canvas_doc is not None

    # Orphans: root-level RectTransforms that are not the Canvas itself.
    orphans = [
        r for r in roots
        if (d := scene.by_id(r)) and d.class_id == RECT_TRANSFORM and r != canvas_rt
    ]
    if not orphans:
        print(f"{scene.path}: no orphaned UI — every RectTransform is already parented.")
        return 0

    named = {name_of_transform(scene, r): r for r in orphans}

    # An explicit order is a design decision (uGUI paints siblings in order, so
    # the last child is on top). Without one, keep the scene's own root order
    # rather than inventing a layering.
    if args.order:
        wanted = [w.strip() for w in args.order.split(",") if w.strip()]
        unknown = [w for w in wanted if w not in named]
        if unknown:
            sys.exit(f"--order names objects that are not orphaned UI: {', '.join(unknown)}\n"
                     f"available: {', '.join(named)}")
        missing = [n for n in named if n not in wanted]
        if missing:
            sys.exit(f"--order must list every orphan; missing: {', '.join(missing)}")
        ordered = [named[w] for w in wanted]
    else:
        ordered = orphans

    existing = get_list(canvas_doc, "m_Children")
    print(f"{scene.path}")
    print(f"  Canvas '{canvas_name}' (&{canvas_rt}) currently has {len(existing)} child(ren)")
    print(f"  adopting {len(ordered)} orphaned UI root(s), back to front:")
    for r in ordered:
        print(f"    {name_of_transform(scene, r):<20} &{r}")

    if args.dry_run:
        print("  (dry run — nothing written)")
        return 0

    # 1. Child points at its new parent.
    for r in ordered:
        child = scene.by_id(r)
        assert child is not None
        if not set_field(child, "m_Father", f"{{fileID: {canvas_rt}}}"):
            sys.exit(f"could not rewrite m_Father on &{r}")

    # 2. Parent lists its new children.
    if not set_list(canvas_doc, "m_Children", existing + ordered):
        sys.exit("could not rewrite m_Children on the Canvas")

    # 3. They are no longer scene roots.
    if not set_list(roots_doc, "m_Roots", [r for r in roots if r not in set(ordered)]):
        sys.exit("could not rewrite m_Roots")

    scene.save()
    print(f"  written. {len(get_list(roots_doc, 'm_Roots'))} root object(s) remain.")
    return 0


def cmd_verify(scene: Scene, _args: argparse.Namespace) -> int:
    """Check the invariants a hand edit is most likely to break."""
    problems: list[str] = []
    roots = set(get_list(scene_roots_doc(scene), "m_Roots"))

    for cls in (TRANSFORM, RECT_TRANSFORM):
        for t in scene.of_class(cls):
            father = ref_id(get_field(t, "m_Father"))
            name = name_of_transform(scene, t.file_id)

            if father == 0 and t.file_id not in roots:
                problems.append(f"{name} (&{t.file_id}) has no parent but is not in m_Roots")
            if father != 0 and t.file_id in roots:
                problems.append(f"{name} (&{t.file_id}) is parented but still listed in m_Roots")
            if father != 0:
                parent = scene.by_id(father)
                if parent is None:
                    problems.append(f"{name} (&{t.file_id}) has a dangling m_Father {father}")
                elif t.file_id not in get_list(parent, "m_Children"):
                    problems.append(
                        f"{name} (&{t.file_id}) claims parent "
                        f"{name_of_transform(scene, father)} which does not list it"
                    )
            for child in get_list(t, "m_Children"):
                cd = scene.by_id(child)
                if cd is None:
                    problems.append(f"{name} (&{t.file_id}) lists a dangling child {child}")
                elif ref_id(get_field(cd, "m_Father")) != t.file_id:
                    problems.append(
                        f"{name} (&{t.file_id}) lists child "
                        f"{name_of_transform(scene, child)} which points elsewhere"
                    )

    for r in roots:
        if scene.by_id(r) is None:
            problems.append(f"m_Roots references a missing object {r}")

    # Orphaned UI is the specific failure this tool exists for.
    for r in roots:
        d = scene.by_id(r)
        if d and d.class_id == RECT_TRANSFORM:
            problems.append(
                f"{name_of_transform(scene, r)} (&{r}) is a RectTransform at the scene root — "
                f"uGUI will not draw it (run adopt-orphan-ui)"
            )

    if problems:
        print(f"{scene.path}: {len(problems)} problem(s)")
        for p in problems:
            print(f"  - {p}")
        return 1
    print(f"{scene.path}: hierarchy consistent")
    return 0


COMMANDS = {
    "list-roots": cmd_list_roots,
    "adopt-orphan-ui": cmd_adopt_orphan_ui,
    "verify": cmd_verify,
}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=sorted(COMMANDS))
    ap.add_argument("scene", type=Path)
    ap.add_argument("--dry-run", action="store_true", help="print the plan, write nothing")
    ap.add_argument("--canvas", help="Canvas to adopt into, when the scene has several")
    ap.add_argument("--order", help="comma-separated child order, back to front")
    args = ap.parse_args()

    if not args.scene.exists():
        sys.exit(f"no such file: {args.scene}")
    return COMMANDS[args.command](Scene.load(args.scene), args)


if __name__ == "__main__":
    raise SystemExit(main())
