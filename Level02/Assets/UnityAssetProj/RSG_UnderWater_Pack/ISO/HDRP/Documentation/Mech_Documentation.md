# ISO Mech Documentation

## Table of Contents
1. Overview
2. Supported Unity Version & Pipeline
3. Included Asset
4. Technical Specifications
5. Setup Instructions
6. LOD & Material Notes
7. Performance Notes
8. Support

---

## 1. Overview

**ISO Mech** is a fully rigged and animated mechanical character designed for isometric (ISO) and top-down camera perspectives.
The asset is optimized for real-time use and uses a **single shared material across all LODs** for optimal performance.

This asset contains **models, materials, textures, and animations**.  
No custom scripts are required.

---

## 2. Supported Unity Version & Pipeline

- **Render Pipeline:** Unity HDRP  
- **Tested Unity Version:** 6000.2.14f  
- **Formats:** Unity, FBX  

> Use in URP or Built-in Render Pipeline may require manual material conversion.

---

## 3. Included Asset

- ISO Mech (LOD0, LOD1)
- Single shared material
- Rigged skeleton
- Animation set
- HDRP-configured prefab

---

## 4. Technical Specifications

### Mech

- **Polygon Count:**  
  - LOD0: ~26k triangles  
  - LOD1: ~18k triangles  

- **Materials:**  
  - **1 material shared between LOD0 and LOD1**

- **Textures:**  
  - Resolution: 4K  
  - Format: PNG  
  - Maps:
    - Albedo (Transparency)
    - Mask Map (HDRP)
    - Normal

- **Rig:** Fully rigged  
- **Animations:** Included

---

## 5. Setup Instructions

1. Import the asset into a Unity HDRP project.
2. Drag the **ISO Mech prefab** into your scene.
3. Verify HDRP is enabled in **Project Settings**.
4. Control animations using the Animator component.

No scripting is required.

---

## 6. LOD & Material Notes

- LODs are configured using Unity LOD Groups.
- **Both LOD0 and LOD1 use the same material and texture set.**
- This setup improves batching and reduces draw calls in isometric scenes.

---

## 7. Performance Notes

- Optimized for isometric and top-down camera views.
- Single-material workflow improves rendering efficiency.
- Suitable for PC and console platforms.

---

## 8. Support

For questions or issues, please contact the publisher via the store page.

---

© ISO Mech Asset
