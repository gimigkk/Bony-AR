# Bony-AR 🦴

An optimized, interactive Augmented Reality (AR) application built with Unity and AR Foundation. This app allows users to project a high-fidelity 3D human skeleton into the real world, interact with individual bones to learn their medical names, and play an interactive anatomy quiz minigame.

## Features
- **Real-World Projection**: Tap to place a fully detailed human skeleton on real-world planes.
- **Interactive Anatomy**: Tap on any of the 206 bones to highlight it and view its Indonesian medical name.
- **Quiz Minigame**: Test your anatomy knowledge by finding randomly prompted bones before the timer runs out!
- **Gesture Controls**: Scale, rotate, and reposition the skeleton using intuitive on-screen sliders and buttons.
- **Hyper-Optimized Performance**: Runs at a locked 60 FPS on mobile. Features GPU Instancing (single draw call for 206 bones), throttled physics raycasting, LayerMask isolation, and explicit high-framerate AR camera configurations.

## Technology Stack
- **Engine**: Unity 2022+ (Built-in / URP)
- **AR Framework**: AR Foundation (ARCore / ARKit)
- **Version Control**: Git

## How to Play
1. Launch the app and slowly pan your camera around your room to detect a flat surface (floor or table).
2. Tap the screen when the indicator appears to place the skeleton.
3. Tap **"Kuis"** to start the minigame!
