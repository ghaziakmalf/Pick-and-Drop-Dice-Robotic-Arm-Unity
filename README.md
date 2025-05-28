# Dice Pick and Drop Robotic Arm Project (IF4063)

A Unity simulation of a Kawasaki BX200L industrial robot performing a dice pick and drop task, utilizing [Flange](https://github.com/Preliy/Flange) package.

**Identity:**
* **Name**: Ghazi Akmal Fauzan
* **NIM**: 13521058

## Brief Description

This project demonstrates the use of Flange package to control a BX200L robot. The robot picks a dice from its current position and drops it at a random XZ location. The dice uses Unity's physics for gravity and rolling.

## Key Features
* **Kawasaki BX200L** robot with **Teach Tool 1** gripper.
* Dice picking from its actual position with a Y-offset.
* Randomized XZ drop position around the robot.
* Controlled gripper orientation.
* Dice simulation with gravity and physics-based rolling.
* Option for robot to return to "home" position after completing the cycle and on game start.
* Robot target movement controlled by `TargetFollower.cs`, sequence logic by `DiceTargetController.cs`.

## How to Run
1.  Open the project in Unity Editor.
2.  Ensure the Flange package is installed.
3.  Open the main scene.
4.  Verify all object references (robot, dice, target, gripper, markers) are assigned in the Inspector for the `SimpleDiceTargetController.cs` script.
5.  Press **Play**.
6.  Press **Spacebar** to start the cycle.

## Dependencies
* Unity Editor (e.g., 2022.3.25f1)
* Flange Package (com.preliy.flange)

---
*Project for the IF4063 - Game Development course.*