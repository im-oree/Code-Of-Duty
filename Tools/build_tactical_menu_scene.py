#!/usr/bin/env python3
"""Materialise the authored TacticalCanvas hierarchy into StartMenu.unity.

This is an editor-time scene authoring tool, not a runtime UI builder. It writes
ordinary uGUI GameObjects into the scene file; CODMainMenu.cs only binds the
saved controls to gameplay services when the scene runs.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

SCENE = Path("Assets/Scenes/StartMenu.unity")
MARKER = "# --- TACTICAL MENU AUTHORING BLOCK ---"
START = 2011000000

# Unity built-in/package script GUIDs already used by this project.
IMAGE_GUID = "fe87c0e1cc204ed48ad3b37840f39efc"
BUTTON_GUID = "4e29b1a8efbd4b44bb3f3716e73f07ff"
TMP_GUID = "f4688fdb7df04437aeb418b961361dc5"
TMP_INPUT_GUID = "2da0c512f12947e489f739169773d7ca"
SCALER_GUID = "0cd44c1031e13a943bb63640046fad76"
RAYCASTER_GUID = "dc42784cf147c0c48a680349fa168899"
FONT_GUID = "8f586378b4e144a9851e7b34d9b748ee"

ACCENT = (1.0, .48, .06, 1.0)
ORANGE_DARK = (.32, .12, .025, .96)
INK = (.025, .032, .042, .96)
PANEL = (.055, .07, .09, .94)
PANEL_SOFT = (.085, .105, .135, .96)
LINE = (.25, .29, .34, .62)
TEXT = (.91, .93, .96, 1.0)
MUTED = (.52, .57, .64, 1.0)


def c(color: tuple[float, float, float, float]) -> str:
    return "{r: %.5g, g: %.5g, b: %.5g, a: %.5g}" % color


def q(value: str) -> str:
    return json.dumps(value, ensure_ascii=False)


class Scene:
    def __init__(self):
        self.next_id = START
        self.docs: list[str] = []
        self.nodes: dict[str, dict] = {}

    def ident(self) -> int:
        self.next_id += 1
        return self.next_id

    def node(self, name: str, parent: dict | None, pos=(0, 0), size=(0, 0),
             anchor_min=(0, 1), anchor_max=(0, 1), pivot=(0, 1),
             image=None, button=False, active=True, raycast=False) -> dict:
        go, rect = self.ident(), self.ident()
        data = {"name": name, "go": go, "rect": rect, "children": [], "components": []}
        # Every RectTransform uses a deferred child-list marker. Track it here
        # (including helper nodes created inside input fields) so the final
        # authored YAML has complete serialized hierarchies.
        all_nodes.append(data)
        if parent is not None:
            parent["children"].append(rect)
        image_id = self.ident() if image is not None else None
        button_id = self.ident() if button else None
        renderer_id = self.ident() if image is not None else None
        data.update(image=image_id, button=button_id, renderer=renderer_id)
        components = [rect]
        if image_id: components.append(image_id)
        if button_id: components.append(button_id)
        if renderer_id: components.append(renderer_id)
        data["components"] = components
        component_lines = "\n".join(f"  - component: {{fileID: {i}}}" for i in components)
        self.docs.append(f"""--- !u!1 &{go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
{component_lines}
  m_Layer: 0
  m_Name: {q(name)}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: {1 if active else 0}
--- !u!224 &{rect}
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children:__CHILDREN_{rect}__
  m_Father: {{fileID: {parent['rect'] if parent else 900000002}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
  m_AnchorMin: {{x: {anchor_min[0]}, y: {anchor_min[1]}}}
  m_AnchorMax: {{x: {anchor_max[0]}, y: {anchor_max[1]}}}
  m_AnchoredPosition: {{x: {pos[0]}, y: {pos[1]}}}
  m_SizeDelta: {{x: {size[0]}, y: {size[1]}}}
  m_Pivot: {{x: {pivot[0]}, y: {pivot[1]}}}
""")
        if image_id:
            self.docs.append(f"""--- !u!114 &{image_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {IMAGE_GUID}, type: 3}}
  m_Name: ""
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.Image
  m_Material: {{fileID: 0}}
  m_Color: {c(image)}
  m_RaycastTarget: {1 if (button or raycast) else 0}
  m_RaycastPadding: {{x: 0, y: 0, z: 0, w: 0}}
  m_Maskable: 1
  m_OnCullStateChanged:
    m_PersistentCalls:
      m_Calls: []
  m_Sprite: {{fileID: 0}}
  m_Type: 0
  m_PreserveAspect: 0
  m_FillCenter: 1
  m_FillMethod: 4
  m_FillAmount: 1
  m_FillClockwise: 1
  m_FillOrigin: 0
  m_UseSpriteMesh: 0
  m_PixelsPerUnitMultiplier: 1
""")
        if button_id:
            self.docs.append(f"""--- !u!114 &{button_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {BUTTON_GUID}, type: 3}}
  m_Name: ""
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.Button
  m_Navigation:
    m_Mode: 3
    m_WrapAround: 0
    m_SelectOnUp: {{fileID: 0}}
    m_SelectOnDown: {{fileID: 0}}
    m_SelectOnLeft: {{fileID: 0}}
    m_SelectOnRight: {{fileID: 0}}
  m_Transition: 1
  m_Colors:
    m_NormalColor: {{r: 1, g: 1, b: 1, a: 1}}
    m_HighlightedColor: {{r: 1, g: 1, b: 1, a: 1}}
    m_PressedColor: {{r: .72, g: .72, b: .72, a: 1}}
    m_SelectedColor: {{r: 1, g: 1, b: 1, a: 1}}
    m_DisabledColor: {{r: .5, g: .5, b: .5, a: .5}}
    m_ColorMultiplier: 1
    m_FadeDuration: .08
  m_SpriteState:
    m_HighlightedSprite: {{fileID: 0}}
    m_PressedSprite: {{fileID: 0}}
    m_SelectedSprite: {{fileID: 0}}
    m_DisabledSprite: {{fileID: 0}}
  m_AnimationTriggers:
    m_NormalTrigger: Normal
    m_HighlightedTrigger: Highlighted
    m_PressedTrigger: Pressed
    m_SelectedTrigger: Selected
    m_DisabledTrigger: Disabled
  m_Interactable: 1
  m_TargetGraphic: {{fileID: {image_id or 0}}}
  m_OnClick:
    m_PersistentCalls:
      m_Calls: []
""")
        if renderer_id:
            self.docs.append(f"""--- !u!222 &{renderer_id}
CanvasRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_CullTransparentMesh: 1
""")
        return data

    def update_components(self, node: dict) -> None:
        """Patch a GameObject document after a late component is authored."""
        for index, document in enumerate(self.docs):
            if not document.startswith(f"--- !u!1 &{node['go']}\nGameObject:"):
                continue
            replacement = "  m_Component:\n" + "\n".join(
                f"  - component: {{fileID: {component}}}" for component in node["components"]
            ) + "\n"
            self.docs[index] = re.sub(r"  m_Component:\n(?:  - component: \{fileID: \d+\}\n)+", replacement, document, count=1)
            return
        raise RuntimeError(f"Could not patch component list for {node['name']}")

    def text(self, name: str, parent: dict, value: str, pos=(0, 0), size=(100, 24),
             color=TEXT, font=16, align=1, bold=False, anchor_min=(0, 1),
             anchor_max=(0, 1), pivot=(0, 1)) -> dict:
        go, rect, text_id, renderer = self.ident(), self.ident(), self.ident(), self.ident()
        parent["children"].append(rect)
        self.docs.append(f"""--- !u!1 &{go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: {rect}}}
  - component: {{fileID: {text_id}}}
  - component: {{fileID: {renderer}}}
  m_Layer: 0
  m_Name: {q(name)}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!224 &{rect}
RectTransform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: {parent['rect']}}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
  m_AnchorMin: {{x: {anchor_min[0]}, y: {anchor_min[1]}}}
  m_AnchorMax: {{x: {anchor_max[0]}, y: {anchor_max[1]}}}
  m_AnchoredPosition: {{x: {pos[0]}, y: {pos[1]}}}
  m_SizeDelta: {{x: {size[0]}, y: {size[1]}}}
  m_Pivot: {{x: {pivot[0]}, y: {pivot[1]}}}
--- !u!114 &{text_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {TMP_GUID}, type: 3}}
  m_Name: ""
  m_EditorClassIdentifier: Unity.TextMeshPro::TMPro.TextMeshProUGUI
  m_Material: {{fileID: 0}}
  m_Color: {c(color)}
  m_RaycastTarget: 0
  m_RaycastPadding: {{x: 0, y: 0, z: 0, w: 0}}
  m_Maskable: 1
  m_OnCullStateChanged:
    m_PersistentCalls:
      m_Calls: []
  m_text: {q(value)}
  m_isRightToLeft: 0
  m_fontAsset: {{fileID: 11400000, guid: {FONT_GUID}, type: 2}}
  m_sharedMaterial: {{fileID: 2180264, guid: {FONT_GUID}, type: 2}}
  m_fontSharedMaterials: []
  m_fontMaterial: {{fileID: 0}}
  m_fontMaterials: []
  m_fontColor32:
    serializedVersion: 2
    rgba: 4294967295
  m_fontColor: {c(color)}
  m_fontSize: {font}
  m_fontSizeBase: {font}
  m_fontWeight: {700 if bold else 400}
  m_enableAutoSizing: 0
  m_fontSizeMin: 12
  m_fontSizeMax: 72
  m_fontStyle: {1 if bold else 0}
  m_HorizontalAlignment: {align}
  m_VerticalAlignment: 512
  m_textAlignment: 65535
  m_characterSpacing: 0
  m_characterHorizontalScale: 1
  m_wordSpacing: 0
  m_lineSpacing: 0
  m_lineSpacingMax: 0
  m_paragraphSpacing: 0
  m_charWidthMaxAdj: 0
  m_TextWrappingMode: 1
  m_wordWrappingRatios: .4
  m_overflowMode: 0
  m_linkedTextComponent: {{fileID: 0}}
  parentLinkedComponent: {{fileID: 0}}
  m_enableKerning: 0
  m_isRichText: 1
  m_isOrthographic: 1
  m_isCullingEnabled: 0
  m_margin: {{x: 0, y: 0, z: 0, w: 0}}
--- !u!222 &{renderer}
CanvasRenderer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go}}}
  m_CullTransparentMesh: 1
""")
        return {"name": name, "go": go, "rect": rect, "text": text_id, "children": []}

    def button(self, name: str, parent: dict, label: str, pos, size, fill=PANEL_SOFT,
               label_color=TEXT, font=14, align=2, **node_kwargs) -> dict:
        item = self.node(name, parent, pos, size, image=fill, button=True, **node_kwargs)
        self.text("Label", item, label, (0, 0), (0, 0), label_color, font, align, True,
                  (0, 0), (1, 1), (.5, .5))
        return item

    def input(self, name: str, parent: dict, placeholder: str, pos, size, initial="") -> dict:
        # Actual TMP input fields, authored with their text/placeholder children.
        outer = self.node(name, parent, pos, size, image=PANEL_SOFT, raycast=True)
        input_id = self.ident()
        outer["components"].append(input_id)
        self.update_components(outer)
        viewport = self.node("Viewport", outer, (12, -4), (-24, -8), (0, 0), (1, 1), (.5, .5))
        value = self.text("Text", viewport, initial, (0, 0), (0, 0), TEXT, 15, 1, False,
                          (0, 0), (1, 1), (.5, .5))
        place = self.text("Placeholder", viewport, placeholder, (0, 0), (0, 0), MUTED, 15, 1, False,
                          (0, 0), (1, 1), (.5, .5))
        self.docs.append(f"""--- !u!114 &{input_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {outer['go']}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {TMP_INPUT_GUID}, type: 3}}
  m_Name: ""
  m_EditorClassIdentifier: Unity.TextMeshPro::TMPro.TMP_InputField
  m_Navigation:
    m_Mode: 3
    m_WrapAround: 0
    m_SelectOnUp: {{fileID: 0}}
    m_SelectOnDown: {{fileID: 0}}
    m_SelectOnLeft: {{fileID: 0}}
    m_SelectOnRight: {{fileID: 0}}
  m_Transition: 1
  m_Colors:
    m_NormalColor: {{r: 1, g: 1, b: 1, a: 1}}
    m_HighlightedColor: {{r: 1, g: 1, b: 1, a: 1}}
    m_PressedColor: {{r: .8, g: .8, b: .8, a: 1}}
    m_SelectedColor: {{r: 1, g: 1, b: 1, a: 1}}
    m_DisabledColor: {{r: .5, g: .5, b: .5, a: .5}}
    m_ColorMultiplier: 1
    m_FadeDuration: .1
  m_SpriteState:
    m_HighlightedSprite: {{fileID: 0}}
    m_PressedSprite: {{fileID: 0}}
    m_SelectedSprite: {{fileID: 0}}
    m_DisabledSprite: {{fileID: 0}}
  m_AnimationTriggers:
    m_NormalTrigger: Normal
    m_HighlightedTrigger: Highlighted
    m_PressedTrigger: Pressed
    m_SelectedTrigger: Selected
    m_DisabledTrigger: Disabled
  m_Interactable: 1
  m_TargetGraphic: {{fileID: {outer['image']}}}
  m_TextViewport: {{fileID: {viewport['rect']}}}
  m_TextComponent: {{fileID: {value['text']}}}
  m_Placeholder: {{fileID: {place['text']}}}
  m_VerticalScrollbar: {{fileID: 0}}
  m_VerticalScrollbarEventHandler: {{fileID: 0}}
  m_LayoutGroup: {{fileID: 0}}
  m_ScrollSensitivity: 1
  m_ContentType: 0
  m_InputType: 0
  m_AsteriskChar: 42
  m_KeyboardType: 0
  m_LineType: 0
  m_HideMobileInput: 0
  m_HideSoftKeyboard: 0
  m_CharacterValidation: 0
  m_RegexValue: ""
  m_GlobalPointSize: 15
  m_CharacterLimit: 0
  m_OnEndEdit:
    m_PersistentCalls:
      m_Calls: []
  m_OnSubmit:
    m_PersistentCalls:
      m_Calls: []
  m_OnSelect:
    m_PersistentCalls:
      m_Calls: []
  m_OnDeselect:
    m_PersistentCalls:
      m_Calls: []
  m_OnTextSelection:
    m_PersistentCalls:
      m_Calls: []
  m_OnEndTextSelection:
    m_PersistentCalls:
      m_Calls: []
  m_OnValueChanged:
    m_PersistentCalls:
      m_Calls: []
  m_OnTouchScreenKeyboardStatusChanged:
    m_PersistentCalls:
      m_Calls: []
  m_CaretColor: {c(ACCENT)}
  m_CustomCaretColor: 0
  m_SelectionColor: {{r: 1, g: .48, b: .06, a: .35}}
  m_Text: {q(initial)}
  m_CaretBlinkRate: .85
  m_CaretWidth: 1
  m_ReadOnly: 0
  m_RichText: 1
  m_GlobalFontAsset: {{fileID: 0}}
  m_OnFocusSelectAll: 1
  m_ResetOnDeActivation: 1
  m_KeepTextSelectionVisible: 0
  m_RestoreOriginalTextOnEscape: 1
  m_isRichTextEditingAllowed: 0
  m_LineLimit: 0
  isAlert: 0
  m_InputValidator: {{fileID: 0}}
  m_ShouldActivateOnSelect: 1
""")
        return outer

    def finish(self) -> str:
        block = MARKER + "\n" + "".join(self.docs)
        for node in self.nodes.values():
            pass
        # Children are known only after building all descendant docs. Replace
        # every temporary marker in the accumulated YAML at the end.
        for doc_node in all_nodes:
            marker = f"__CHILDREN_{doc_node['rect']}__"
            children = doc_node["children"]
            children_yaml = "[]" if not children else "\n" + "\n".join(f"  - {{fileID: {kid}}}" for kid in children)
            block = block.replace(marker, children_yaml)
        return block


all_nodes: list[dict] = []


def register(node: dict) -> dict:
    all_nodes.append(node)
    return node


def n(scene: Scene, *args, **kwargs):
    return scene.node(*args, **kwargs)


def b(scene: Scene, *args, **kwargs):
    return scene.button(*args, **kwargs)


def t(scene: Scene, *args, **kwargs):
    return scene.text(*args, **kwargs)


def i(scene: Scene, *args, **kwargs):
    return scene.input(*args, **kwargs)


def divider(scene: Scene, parent: dict, pos, size):
    return n(scene, "Divider", parent, pos, size, image=LINE)


def title(scene: Scene, parent: dict, kicker: str, heading: str, desc: str):
    t(scene, "Kicker", parent, kicker, (48, -30), (650, 18), ACCENT, 12, 1, True)
    t(scene, "Heading", parent, heading, (48, -56), (720, 48), TEXT, 31, 1, True)
    t(scene, "Description", parent, desc, (48, -103), (760, 38), MUTED, 14, 1)
    divider(scene, parent, (48, -148), (760, 1))


def add_operation(scene: Scene, parent: dict, name: str, title_text: str, desc: str, y: int, code: str):
    card = b(scene, name, parent, "", (48, y), (530, 73), PANEL)
    n(scene, "Accent", card, (0, 0), (5, 73), image=ACCENT)
    t(scene, "Code", card, code, (22, -14), (70, 18), ACCENT, 11, 1, True)
    t(scene, "Title", card, title_text, (22, -33), (270, 24), TEXT, 17, 1, True)
    t(scene, "Description", card, desc, (22, -56), (420, 16), MUTED, 12, 1)
    t(scene, "Arrow", card, "›", (480, -24), (28, 28), ACCENT, 26, 2, True)
    return card


def add_weapon(scene: Scene, parent: dict, name: str, display: str, desc: str, x: int, y: int):
    card = b(scene, name, parent, "", (x, y), (225, 148), PANEL)
    t(scene, "Type", card, "WEAPON PLATFORM", (18, -16), (180, 15), ACCENT, 10, 1, True)
    t(scene, "Name", card, display, (18, -42), (190, 27), TEXT, 18, 1, True)
    t(scene, "Desc", card, desc, (18, -76), (185, 38), MUTED, 12, 1)
    t(scene, "Metric", card, "CUSTOM READY", (18, -119), (160, 16), MUTED, 10, 1, True)
    n(scene, "SelectedBar", card, (0, -144), (225, 4), image=ACCENT)
    return card


def add_setting(scene: Scene, parent: dict, key: str, label: str, value: str, y: int,
                has_minus_plus=False, one_button=None):
    row = n(scene, key + "Row", parent, (0, y), (0, 36), (0, 1), (1, 1), (.5, 1), image=None)
    t(scene, key + "Label", row, label, (0, -7), (300, 20), TEXT, 14, 1)
    t(scene, key + "Value", row, value, (-152, -7), (112, 20), ACCENT, 13, 4, True, (1, 1), (1, 1), (1, 1))
    if has_minus_plus:
        b(scene, "Button_" + key + "Minus", row, "−", (-35, -3), (28, 28), PANEL_SOFT, TEXT, 16)
        b(scene, "Button_" + key + "Plus", row, "+", (0, -3), (28, 28), PANEL_SOFT, TEXT, 16, 2)
    elif one_button:
        b(scene, "Button_" + key, row, "CHANGE", (-35, -3), (63, 28), PANEL_SOFT, TEXT, 10)
    divider(scene, row, (0, -35), (0, 1))
    return row


def authored_scene() -> tuple[str, dict]:
    global all_nodes
    all_nodes = []
    scene = Scene()

    # Canvas root authored as a child of CODMainMenu, not spawned by C#.
    root = n(scene, "TacticalCanvas", None, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5))
    root["canvas"] = scene.ident()
    root["scaler"] = scene.ident()
    root["raycaster"] = scene.ident()
    root["components"].extend([root["canvas"], root["scaler"], root["raycaster"]])
    scene.update_components(root)

    # Muted screen-overlay to retain legibility over the real 3D operator stage.
    n(scene, "Vignette", root, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5), image=(.005, .008, .014, .36))
    n(scene, "LeftShade", root, (0, 0), (830, 0), (0, 0), (0, 1), (0, .5), image=(.008, .012, .02, .56))

    top = n(scene, "TopNav", root, (0, 0), (0, 82), (0, 1), (1, 1), (.5, 1), image=(.018, .024, .034, .94))
    n(scene, "TopLine", top, (0, -80), (0, 2), (0, 0), (1, 0), (.5, 0), image=ACCENT)
    t(scene, "Brand", top, "COD // STRIKE UNIT", (38, -19), (270, 24), TEXT, 18, 1, True)
    t(scene, "Operation", top, "OPERATION SUNSCORCH", (38, -45), (300, 16), ACCENT, 11, 1, True)
    x = 400
    for tab in ["PLAY", "LOADOUT", "OPERATORS", "CAREER", "SETTINGS"]:
        item = b(scene, "Tab_" + tab, top, tab, (x, -17), (120, 48), (0, 0, 0, 0), MUTED, 13)
        n(scene, "ActiveBar", item, (12, -44), (96, 3), image=ACCENT)
        x += 126
    profile = b(scene, "ProfileButton", top, "", (-192, -16), (128, 50), PANEL_SOFT, TEXT, 12, 1,
                anchor_min=(1, 1), anchor_max=(1, 1), pivot=(1, 1))
    t(scene, "Rank", profile, "RANK 001", (12, -10), (100, 13), ACCENT, 9, 1, True)
    t(scene, "Name", profile, "PLAYER", (12, -27), (105, 18), TEXT, 13, 1, True)
    b(scene, "SettingsButton", top, "⚙", (-58, -17), (34, 34), PANEL_SOFT, TEXT, 16,
      anchor_min=(1, 1), anchor_max=(1, 1), pivot=(1, 1))
    b(scene, "QuitButton", top, "×", (-20, -17), (34, 34), PANEL_SOFT, MUTED, 17,
      anchor_min=(1, 1), anchor_max=(1, 1), pivot=(1, 1))

    screens = n(scene, "Screens", root, (0, -82), (0, -146), (0, 0), (1, 1), (.5, .5))
    screen_data: dict[str, dict] = {}
    for screen_id, active in [("PLAY", True), ("LOADOUT", False), ("OPERATORS", False), ("CAREER", False), ("SETTINGS", False)]:
        screen_data[screen_id] = n(scene, "Screen_" + screen_id, screens, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5), active=active)

    # PLAY -----------------------------------------------------------------
    play = screen_data["PLAY"]
    title(scene, play, "MULTIPLAYER // DEPLOYMENT", "CHOOSE YOUR OPERATION", "Connect to a local squad, host an operation, or enter the arena alone.")
    ops = n(scene, "Operations", play, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5))
    add_operation(scene, ops, "Action_QuickPlay", "QUICK PLAY", "Join the first discovered LAN operation.", -178, "01")
    add_operation(scene, ops, "Action_Host", "CREATE OPERATION", "Host a private local-network room.", -258, "02")
    add_operation(scene, ops, "Action_Browser", "SERVER BROWSER", "Refresh available LAN operations.", -338, "03")
    add_operation(scene, ops, "Action_DirectConnect", "DIRECT CONNECT", "Join a server using its address.", -418, "04")
    add_operation(scene, ops, "Action_Solo", "SOLO TRAINING", "Start a local practice operation.", -498, "05")

    browser = n(scene, "BrowserPanel", play, (-48, -30), (475, 524), (1, 1), (1, 1), (1, 1), image=INK)
    t(scene, "Kicker", browser, "NETWORK // LIVE", (22, -18), (260, 14), ACCENT, 11, 1, True)
    t(scene, "Heading", browser, "LAN OPERATIONS", (22, -40), (360, 26), TEXT, 20, 1, True)
    t(scene, "Status", browser, "SCANNING LOCAL NETWORK…", (22, -70), (390, 18), MUTED, 12, 1)
    divider(scene, browser, (22, -97), (431, 1))
    for row in range(5):
        item = b(scene, "ServerRow_" + str(row), browser, "", (22, -112 - row * 76), (431, 64), PANEL)
        t(scene, "Title", item, "NO SIGNAL", (14, -12), (300, 20), MUTED, 14, 1, True)
        t(scene, "Meta", item, "WAITING FOR LAN BROADCAST", (14, -36), (350, 16), MUTED, 11, 1)
        t(scene, "Join", item, "JOIN ›", (-17, -25), (54, 18), ACCENT, 10, 4, True, (1, 1), (1, 1), (1, 1))

    # LOADOUT --------------------------------------------------------------
    loadout = screen_data["LOADOUT"]
    title(scene, loadout, "ARMORY // PERSONAL KIT", "CUSTOM LOADOUT", "Choose a real weapon platform. Your selection is saved for the next spawn.")
    active_card = n(scene, "ActiveLoadout", loadout, (-48, -37), (340, 110), (1, 1), (1, 1), (1, 1), image=INK)
    t(scene, "Caption", active_card, "ACTIVE CONFIGURATION", (18, -15), (220, 14), ACCENT, 10, 1, True)
    t(scene, "PrimaryLabel", active_card, "PRIMARY", (18, -39), (90, 16), MUTED, 11, 1, True)
    t(scene, "PrimaryValue", active_card, "N4 CARBINE", (105, -39), (205, 16), TEXT, 12, 1, True)
    t(scene, "SecondaryLabel", active_card, "SECONDARY", (18, -66), (90, 16), MUTED, 11, 1, True)
    t(scene, "SecondaryValue", active_card, "GLOK 19", (105, -66), (205, 16), TEXT, 12, 1, True)
    t(scene, "Persist", active_card, "SAVED TO NEXT DEPLOYMENT", (18, -91), (280, 13), ACCENT, 9, 1, True)
    primary = n(scene, "PrimaryWeapons", loadout, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5))
    t(scene, "Label", primary, "PRIMARY WEAPON", (48, -173), (420, 22), ACCENT, 14, 1, True)
    add_weapon(scene, primary, "Weapon_N4_Rifle", "N4 CARBINE", "Balanced automatic rifle.", 48, -205)
    add_weapon(scene, primary, "Weapon_Saga_Rifle", "SAGA AR", "Hard-hitting battle rifle.", 288, -205)
    add_weapon(scene, primary, "Weapon_P6_SMG", "P6 SMG", "Close-range high fire rate.", 528, -205)
    secondary = n(scene, "SecondaryWeapons", loadout, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5))
    t(scene, "Label", secondary, "SECONDARY WEAPON", (48, -385), (420, 22), ACCENT, 14, 1, True)
    add_weapon(scene, secondary, "Weapon_Glok_Pistol", "GLOK 19", "Fast-draw emergency sidearm.", 48, -417)

    # OPERATORS ------------------------------------------------------------
    operators = screen_data["OPERATORS"]
    title(scene, operators, "PERSONNEL // FIELD READY", "SELECT OPERATOR", "Your selected identity appears on the live operator stage and in the next match.")
    cards = n(scene, "OperatorCards", operators, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5))
    for name, display, description, tint, x in [
        ("Operator_Crimson", "CRIMSON", "Battle-worn assault operator.\nBalanced for direct engagements.", (.48, .08, .06, .96), 48),
        ("Operator_Cobalt", "COBALT", "Recon operator in cobalt blue.\nConfigured for tactical flanks.", (.05, .16, .38, .96), 355),
    ]:
        card = b(scene, name, cards, "", (x, -184), (280, 290), PANEL)
        n(scene, "PortraitField", card, (16, -18), (248, 124), image=tint)
        t(scene, "Role", card, "OPERATOR PROFILE", (20, -157), (220, 14), ACCENT, 10, 1, True)
        t(scene, "Name", card, display, (20, -180), (230, 28), TEXT, 21, 1, True)
        t(scene, "Desc", card, description, (20, -215), (225, 45), MUTED, 12, 1)
        n(scene, "SelectedBar", card, (0, -286), (280, 4), image=ACCENT)
    t(scene, "LiveNote", operators, "LIVE OPERATOR STAGE // SKIN AND PRIMARY WEAPON UPDATE IMMEDIATELY", (48, -505), (650, 18), MUTED, 11, 1, True)

    # CAREER ---------------------------------------------------------------
    career = screen_data["CAREER"]
    title(scene, career, "SERVICE RECORD // PROFILE", "OPERATOR CAREER", "Set your callsign and review the currently tracked service record.")
    profile_card = n(scene, "ProfileCard", career, (48, -178), (570, 235), image=INK)
    t(scene, "Caption", profile_card, "IDENTIFICATION", (24, -21), (300, 16), ACCENT, 11, 1, True)
    t(scene, "Prompt", profile_card, "CALLSIGN", (24, -54), (140, 18), TEXT, 14, 1, True)
    i(scene, "Input_ProfileName", profile_card, "ENTER CALLSIGN", (24, -80), (320, 42), "Player")
    b(scene, "Button_SaveProfile", profile_card, "SAVE CALLSIGN", (358, -80), (180, 42), ACCENT, INK, 12)
    divider(scene, profile_card, (24, -142), (522, 1))
    t(scene, "Record", profile_card, "SERVICE RECORD", (24, -163), (180, 15), MUTED, 11, 1, True)
    t(scene, "Stats", profile_card, "MATCHES  --     KILLS  --     DEATHS  --     K/D  --", (24, -190), (500, 22), TEXT, 15, 1, True)
    t(scene, "Note", profile_card, "Live match progression will populate after your first operation.", (24, -215), (500, 14), MUTED, 11, 1)
    challenges = n(scene, "Challenges", career, (-48, -178), (350, 235), (1, 1), (1, 1), (1, 1), image=INK)
    t(scene, "Caption", challenges, "FIELD INTEL", (20, -21), (280, 16), ACCENT, 11, 1, True)
    t(scene, "Heading", challenges, "READY FOR DEPLOYMENT", (20, -55), (290, 26), TEXT, 18, 1, True)
    t(scene, "Desc", challenges, "Complete LAN or solo operations to build your record. Core networking, loadouts, operators and settings are available now.", (20, -93), (300, 72), MUTED, 12, 1)
    t(scene, "Flag", challenges, "NO UNAVAILABLE STORE OR MODE CONTROLS", (20, -197), (300, 15), ACCENT, 9, 1, True)

    # SETTINGS -------------------------------------------------------------
    settings = screen_data["SETTINGS"]
    title(scene, settings, "SYSTEMS // CONFIGURATION", "SETTINGS", "All changes apply instantly and persist between operations.")
    audio = n(scene, "Audio", settings, (48, -175), (370, 90), image=INK)
    t(scene, "Heading", audio, "AUDIO", (20, -16), (300, 22), ACCENT, 14, 1, True)
    add_setting(scene, audio, "Master", "MASTER VOLUME", "100%", -44, True)

    video = n(scene, "Video", settings, (48, -282), (580, 315), image=INK)
    t(scene, "Heading", video, "VIDEO // URP", (20, -16), (300, 22), ACCENT, 14, 1, True)
    add_setting(scene, video, "Fov", "FIELD OF VIEW", "70°", -44, True)
    add_setting(scene, video, "Render", "RENDER SCALE", "100%", -80, True)
    add_setting(scene, video, "Shadow", "SHADOW DISTANCE", "50 M", -116, True)
    add_setting(scene, video, "Quality", "QUALITY PRESET", "DEFAULT", -152, False, True)
    add_setting(scene, video, "MSAA", "ANTI-ALIASING", "4X", -188, False, True)
    add_setting(scene, video, "VSync", "V-SYNC", "ON", -224, False, True)
    add_setting(scene, video, "Fullscreen", "FULLSCREEN", "ON", -260, False, True)

    controls = n(scene, "Controls", settings, (650, -175), (370, 243), image=INK)
    t(scene, "Heading", controls, "MOUSE & CONTROLS", (20, -16), (300, 22), ACCENT, 14, 1, True)
    add_setting(scene, controls, "Sensitivity", "MOUSE SENSITIVITY", "1.0", -44, True)
    add_setting(scene, controls, "Crouch", "CROUCH STYLE", "TOGGLE", -80, False, True)
    add_setting(scene, controls, "TacSprint", "TAC SPRINT", "DOUBLE TAP", -116, False, True)
    add_setting(scene, controls, "FireSprint", "FIRE WHILE SPRINTING", "OFF", -152, False, True)

    keybinds = n(scene, "Keybinds", settings, (-48, -282), (590, 314), (1, 1), (1, 1), (1, 1), image=INK)
    t(scene, "Heading", keybinds, "KEYBINDS", (20, -16), (300, 22), ACCENT, 14, 1, True)
    action_defs = [
        ("sprint", "SPRINT"), ("jump", "JUMP"), ("crouch", "CROUCH / SLIDE"), ("fire", "FIRE WEAPON"),
        ("aim", "AIM DOWN SIGHTS"), ("sightSwitch", "SWITCH SIGHT"), ("weapon1", "PRIMARY WEAPON"),
        ("weapon2", "SECONDARY WEAPON"), ("melee", "MELEE STANCE"), ("reload", "RELOAD"),
        ("grenade", "THROW GRENADE"), ("interact", "INTERACT / PICKUP"), ("viewToggle", "FPS / TPS VIEW"),
        ("lean", "LEAN"), ("pause", "PAUSE MENU"),
    ]
    for idx, (code, label) in enumerate(action_defs):
        col, row = idx % 2, idx // 2
        bind = b(scene, "Bind_" + code, keybinds, "", (20 + col * 281, -48 - row * 31), (266, 27), PANEL)
        t(scene, "Action", bind, label, (10, -5), (165, 17), TEXT, 10, 1, True)
        t(scene, "Value", bind, "--", (-10, -5), (72, 17), ACCENT, 10, 4, True, (1, 1), (1, 1), (1, 1))
    b(scene, "Button_ResetBinds", keybinds, "RESET ALL KEYBINDS", (20, -291), (190, 28), ORANGE_DARK, TEXT, 10)

    # OVERLAYS -------------------------------------------------------------
    overlays = n(scene, "Overlays", root, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5))
    overlay_back = (.005, .008, .015, .82)
    host = n(scene, "HostDialog", overlays, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5), image=overlay_back, active=False, raycast=True)
    host_panel = n(scene, "Panel", host, (0, 0), (490, 310), (.5, .5), (.5, .5), (.5, .5), image=INK)
    t(scene, "Kicker", host_panel, "NETWORK // HOST", (30, -30), (380, 16), ACCENT, 11, 1, True)
    t(scene, "Heading", host_panel, "CREATE OPERATION", (30, -57), (400, 35), TEXT, 24, 1, True)
    t(scene, "RoomPrompt", host_panel, "ROOM NAME", (30, -111), (180, 16), MUTED, 11, 1, True)
    i(scene, "Input_HostRoom", host_panel, "YOUR OPERATION", (30, -133), (430, 42), "")
    t(scene, "CapPrompt", host_panel, "PLAYER CAPACITY", (30, -190), (180, 16), MUTED, 11, 1, True)
    i(scene, "Input_HostCapacity", host_panel, "8", (30, -212), (132, 42), "8")
    b(scene, "Button_Host", host_panel, "HOST LAN OPERATION", (250, -212), (210, 42), ACCENT, INK, 12)
    b(scene, "Button_Cancel", host_panel, "CANCEL", (30, -264), (120, 28), PANEL_SOFT, TEXT, 10)

    connect = n(scene, "ConnectDialog", overlays, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5), image=overlay_back, active=False, raycast=True)
    connect_panel = n(scene, "Panel", connect, (0, 0), (490, 265), (.5, .5), (.5, .5), (.5, .5), image=INK)
    t(scene, "Kicker", connect_panel, "NETWORK // ADDRESS", (30, -30), (380, 16), ACCENT, 11, 1, True)
    t(scene, "Heading", connect_panel, "DIRECT CONNECT", (30, -57), (400, 35), TEXT, 24, 1, True)
    t(scene, "Prompt", connect_panel, "SERVER ADDRESS OR HOSTNAME", (30, -111), (300, 16), MUTED, 11, 1, True)
    i(scene, "Input_Address", connect_panel, "192.168.0.10", (30, -133), (430, 42), "")
    b(scene, "Button_Connect", connect_panel, "CONNECT", (250, -190), (210, 42), ACCENT, INK, 12)
    b(scene, "Button_Cancel", connect_panel, "CANCEL", (30, -190), (120, 28), PANEL_SOFT, TEXT, 10)

    lobby = n(scene, "LobbyOverlay", overlays, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5), image=overlay_back, active=False, raycast=True)
    lobby_panel = n(scene, "Panel", lobby, (0, 0), (560, 580), (.5, .5), (.5, .5), (.5, .5), image=INK)
    t(scene, "Kicker", lobby_panel, "SQUAD // STAGING", (30, -28), (400, 16), ACCENT, 11, 1, True)
    t(scene, "Heading", lobby_panel, "OPERATION LOBBY", (30, -55), (400, 35), TEXT, 24, 1, True)
    t(scene, "Brief", lobby_panel, "Wait for the squad, then deploy when the host is ready.", (30, -91), (480, 18), MUTED, 12, 1)
    roster = n(scene, "Roster", lobby, (0, 0), (0, 0), (0, 0), (1, 1), (.5, .5))
    # Roster is rooted under overlay rather than panel, specifically so its paths remain stable for the binder.
    for row in range(8):
        item = n(scene, "PlayerRow_" + str(row), roster, (0, 130 - row * 43), (500, 37), (.5, .5), (.5, .5), (.5, .5), image=PANEL)
        t(scene, "Name", item, "[ WAITING ]", (16, -9), (450, 20), TEXT, 13, 1, True)
    # Direct scene buttons at the visual panel bottom.
    b(scene, "Button_Start", lobby, "DEPLOY OPERATION", (-8, -235), (230, 42), ACCENT, INK, 12, 2,
      anchor_min=(.5, .5), anchor_max=(.5, .5), pivot=(.5, .5))
    b(scene, "Button_Leave", lobby, "LEAVE LOBBY", (-8, -286), (180, 30), PANEL_SOFT, TEXT, 10, 2,
      anchor_min=(.5, .5), anchor_max=(.5, .5), pivot=(.5, .5))

    toast = n(scene, "Toast", root, (0, -98), (400, 34), (.5, 1), (.5, 1), (.5, 1), image=INK, active=False)
    t(scene, "Message", toast, "", (0, 0), (0, 0), ACCENT, 12, 2, True, (0, 0), (1, 1), (.5, .5))
    footer = n(scene, "Footer", root, (0, 0), (0, 64), (0, 0), (1, 0), (.5, 0), image=(.018, .024, .034, .9))
    t(scene, "Hint", footer, "LAN READY  //  OPERATORS ONLINE  //  SUNSCORCH DEPOT IN ROTATION", (38, 20), (700, 18), MUTED, 11, 1, True, (0, 0), (0, 0), (0, 0))
    t(scene, "Version", footer, "BUILD 0.1  •  LOCAL NETWORK", (-38, 20), (280, 18), MUTED, 10, 4, True, (1, 0), (1, 0), (1, 0))

    # Canvas components appended after regular components so the generated
    # GameObject remains an ordinary saved uGUI Canvas.
    scene.docs.append(f"""--- !u!223 &{root['canvas']}
Canvas:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {root['go']}}}
  m_Enabled: 1
  serializedVersion: 3
  m_RenderMode: 0
  m_Camera: {{fileID: 0}}
  m_PlaneDistance: 100
  m_PixelPerfect: 0
  m_ReceivesEvents: 1
  m_OverrideSorting: 0
  m_OverridePixelPerfect: 0
  m_SortingBucketNormalizedSize: 0
  m_VertexColorAlwaysGammaSpace: 0
  m_UseReflectionProbes: 0
  m_AdditionalShaderChannelsFlag: 0
  m_UpdateRectTransformForStandalone: 0
  m_SortingLayerID: 0
  m_SortingOrder: 75
  m_TargetDisplay: 0
--- !u!114 &{root['scaler']}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {root['go']}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {SCALER_GUID}, type: 3}}
  m_Name: ""
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.CanvasScaler
  m_UiScaleMode: 1
  m_ReferencePixelsPerUnit: 100
  m_ScaleFactor: 1
  m_ReferenceResolution: {{x: 1920, y: 1080}}
  m_ScreenMatchMode: 0
  m_MatchWidthOrHeight: .5
  m_PhysicalUnit: 3
  m_FallbackScreenDPI: 96
  m_DefaultSpriteDPI: 96
  m_DynamicPixelsPerUnit: 1
  m_PresetInfoIsWorld: 0
--- !u!114 &{root['raycaster']}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {root['go']}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {RAYCASTER_GUID}, type: 3}}
  m_Name: ""
  m_EditorClassIdentifier: UnityEngine.UI::UnityEngine.UI.GraphicRaycaster
  m_IgnoreReversedGraphics: 1
  m_BlockingObjects: 0
  m_BlockingMask:
    serializedVersion: 2
    m_Bits: 4294967295
""")
    return scene.finish(), root


def apply_scene() -> None:
    text = SCENE.read_text()
    if MARKER in text:
        raise SystemExit("TacticalCanvas block is already present; refusing to duplicate authored objects.")
    block, root = authored_scene()

    # Disable only the known legacy Canvas object, retaining it for recovery
    # without allowing its former runtime builder to overlap TacticalCanvas.
    legacy_pattern = r"(m_Name: MenuUI\n(?:.*\n){0,8}?  m_IsActive: )1"
    text, changed = re.subn(legacy_pattern, r"\g<1>0", text, count=1)
    if changed != 1:
        raise SystemExit("Could not locate legacy MenuUI active flag.")

    old = "  m_Children:\n  - {fileID: 1879613636}\n  m_Father: {fileID: 0}"
    new = f"  m_Children:\n  - {{fileID: 1879613636}}\n  - {{fileID: {root['rect']}}}\n  m_Father: {{fileID: 0}}"
    if old not in text:
        raise SystemExit("Could not locate CODMainMenu root transform children.")
    text = text.replace(old, new, 1)
    SCENE.write_text(text.rstrip() + "\n" + block)
    print(f"Authored TacticalCanvas with {len(all_nodes)} direct scene objects into {SCENE}.")


if __name__ == "__main__":
    apply_scene()
