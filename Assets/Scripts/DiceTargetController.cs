using UnityEngine;
using System.Collections;
using Preliy.Flange.Common; // For Gripper, Part (if needed for gripper interaction)
using Preliy.Flange;       // For Matrix4x4 extensions like GetMatrix()

public class SimpleDiceTargetController : MonoBehaviour
{
    [Header("Target & Robot References")]
    [Tooltip("The GameObject that the robot's TargetFollower script is tracking.")]
    public Transform liveRobotTcpTarget; // Assign your "LiveRobotTCPTarget" GameObject here

    [Tooltip("The dice GameObject.")]
    public Transform diceTransform;

    [Tooltip("The base or center point of the robot (e.g., the root transform of BX200L).")]
    public Transform robotBaseReference;

    [Tooltip("Marker for the robot's home position (e.g., where it starts or returns to).")]
    public Transform homePositionMarker; // Optional, can be null if not used

    [Tooltip("Whether to return to home position after sequence. If false, will not return home.")]
    public bool returnHome = true; // Whether to return to home position after sequence

    [Tooltip("The Gripper component on the robot's tool.")]
    public Gripper gripper; // Assign the Gripper script from your robot's tool

    [Header("Movement & Operation Settings")]
    [Tooltip("Speed of the live target's movement (units per second).")]
    public float targetMoveSpeed = 0.5f; // Meters per second

    [Tooltip("Speed of the live target's rotation (degrees per second).")]
    public float targetRotateSpeed = 90f; // Degrees per second

    [Tooltip("Vertical offset from the dice's center to target for picking (e.g., -0.05 for 50mm below center).")]
    public float pickTargetYOffsetFromDiceCenter = -0.05f;

    [Tooltip("Vertical offset from the calculated pick Y for the approach/lift position.")]
    public float approachLiftVerticalOffset = 0.1f; // 10cm above the actual pick Y

    [Tooltip("Minimum radius for random drop position from robot base.")]
    public float dropRadiusMin = 0.4f;

    [Tooltip("Maximum radius for random drop position from robot base.")]
    public float dropRadiusMax = 0.8f;

    [Tooltip("Fixed Y height for the drop position relative to the robot base's Y.")]
    public float dropTargetYRelativeToBase = 0.1f; // e.g., 10cm above robot base Y level

    [Tooltip("Time to wait after gripping/releasing.")]
    public float gripOperationTime = 0.75f;

    [Tooltip("Time to wait for the dice to settle after being rolled.")]
    public float diceSettleTime = 3.0f;

    [Tooltip("Force applied to roll the dice.")]
    public float diceRollForce = 5f;

    [Tooltip("Torque applied to roll the dice.")]
    public float diceTorqueForce = 10f;


    private Rigidbody diceRigidbody;
    private Coroutine activeSequenceCoroutine;

    private enum SequenceState { Idle, MovingToPickApproach, MovingToPick, Gripping, LiftingFromPick, MovingToDropApproach, MovingToDrop, Releasing, LiftingFromDrop, Rolling, ReturningHome }
    private SequenceState currentSequenceState = SequenceState.Idle;


    void Start()
    {
        if (liveRobotTcpTarget == null) Debug.LogError("LiveRobotTcpTarget not assigned!");
        if (diceTransform == null) Debug.LogError("DiceTransform not assigned!");
        else
        {
            diceRigidbody = diceTransform.GetComponent<Rigidbody>();
            if (diceRigidbody == null) Debug.LogError("Dice is missing a Rigidbody!");
            else
            {
                diceRigidbody.useGravity = true;
                diceRigidbody.isKinematic = false; // Start as non-kinematic
            }
            // Ensure 'Part' component is on the dice if your Gripper script needs it
            if (diceTransform.GetComponent<Part>() == null)
            {
                diceTransform.gameObject.AddComponent<Part>();
                Debug.Log("Added 'Part' component to dice.");
            }
        }
        if (robotBaseReference == null) Debug.LogError("RobotBaseReference not assigned!");
        if (gripper == null) Debug.LogError("Gripper not assigned!");

        // Set initial position of liveRobotTcpTarget to a safe home position (optional)
        // For example, if you have a homePositionMarker:
        // if (homePositionMarker != null) {
        //     liveRobotTcpTarget.SetPositionAndRotation(homePositionMarker.position, homePositionMarker.rotation);
        // }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && currentSequenceState == SequenceState.Idle)
        {
            if (activeSequenceCoroutine != null)
            {
                StopCoroutine(activeSequenceCoroutine);
            }
            activeSequenceCoroutine = StartCoroutine(PickAndDropSequence());
        }
    }

    // Coroutine to smoothly move and rotate the liveRobotTcpTarget
    IEnumerator MoveAndOrientTarget(Vector3 targetPosition, Quaternion targetOrientation)
    {
        if (liveRobotTcpTarget == null) yield break;

        // Ensure TargetFollower knows the desired orientation for the robot's TCP
        // The TargetFollower script will use this liveRobotTcpTarget's transform.
        // The orientation logic (Z to robot base, Y down) is now handled here before passing to TargetFollower.

        while (Vector3.Distance(liveRobotTcpTarget.position, targetPosition) > 0.01f ||
               Quaternion.Angle(liveRobotTcpTarget.rotation, targetOrientation) > 1.0f)
        {
            liveRobotTcpTarget.position = Vector3.MoveTowards(liveRobotTcpTarget.position, targetPosition, targetMoveSpeed * Time.deltaTime);
            liveRobotTcpTarget.rotation = Quaternion.RotateTowards(liveRobotTcpTarget.rotation, targetOrientation, targetRotateSpeed * Time.deltaTime);
            yield return null; // Wait for the next frame
        }
        // Snap to final position/rotation to ensure precision
        liveRobotTcpTarget.SetPositionAndRotation(targetPosition, targetOrientation);
    }

    // Calculates the desired orientation for the gripper
    Quaternion CalculateGripperOrientation(Vector3 currentTargetPosition)
    {
        // Sumbu Y lokal gripper (hijau) harus mengarah ke bawah (Vector3.down global).
        Vector3 gripperLocalY_pointsTo = Vector3.down;

        // Sumbu Z lokal gripper (biru, depan) harus mengarah secara horizontal ke dasar robot.
        Vector3 gripperLocalZ_pointsTo;
        if (robotBaseReference != null)
        {
            Vector3 directionToRobotBase = (robotBaseReference.position - currentTargetPosition);
            directionToRobotBase.y = 0; // Proyeksikan ke bidang XZ (horizontal)
            if (directionToRobotBase.sqrMagnitude < 0.0001f)
            {
                gripperLocalZ_pointsTo = robotBaseReference.forward; // Fallback jika tepat di bawah/atas
            }
            else
            {
                gripperLocalZ_pointsTo = directionToRobotBase.normalized;
            }
        }
        else
        {
            gripperLocalZ_pointsTo = Vector3.forward; // Fallback
        }
        return Quaternion.LookRotation(gripperLocalZ_pointsTo, gripperLocalY_pointsTo);
    }

    IEnumerator PickAndDropSequence()
    {
        currentSequenceState = SequenceState.MovingToPickApproach;
        Debug.Log("State: Moving to Pick Approach");

        // 1. Calculate Pick Poses
        Vector3 diceCurrentCenter = diceTransform.position;
        Vector3 actualPickTargetPos = new Vector3(diceCurrentCenter.x, diceCurrentCenter.y + pickTargetYOffsetFromDiceCenter, diceCurrentCenter.z);
        Quaternion pickOrientation = CalculateGripperOrientation(actualPickTargetPos);

        Vector3 pickApproachPos = new Vector3(actualPickTargetPos.x, actualPickTargetPos.y + approachLiftVerticalOffset, actualPickTargetPos.z);

        // Move to approach pick position
        yield return StartCoroutine(MoveAndOrientTarget(pickApproachPos, pickOrientation));

        currentSequenceState = SequenceState.MovingToPick;
        Debug.Log("State: Moving to Pick");
        // Move to actual pick position
        yield return StartCoroutine(MoveAndOrientTarget(actualPickTargetPos, pickOrientation));

        currentSequenceState = SequenceState.Gripping;
        Debug.Log("State: Gripping");
        gripper.Grip(true); // This should parent the dice and make it kinematic via TargetFollower's robot
        // The Gripper script from Flange should handle the dice's Rigidbody and parent status.
        // If not, you might need to:
        // diceRigidbody.isKinematic = true;
        // diceTransform.SetParent(gripper.transform); // or a specific grab point on the gripper
        yield return new WaitForSeconds(gripOperationTime);

        currentSequenceState = SequenceState.LiftingFromPick;
        Debug.Log("State: Lifting from Pick");
        // Lift dice (move to the same approach position)
        Vector3 liftFromPickPos = new Vector3(actualPickTargetPos.x, actualPickTargetPos.y + approachLiftVerticalOffset, actualPickTargetPos.z);
        yield return StartCoroutine(MoveAndOrientTarget(liftFromPickPos, pickOrientation));

        // 2. Calculate Random Drop Pose
        Vector2 randomCircle = Random.insideUnitCircle.normalized * Random.Range(dropRadiusMin, dropRadiusMax);
        Vector3 dropOffsetFromBase = new Vector3(randomCircle.x, 0, randomCircle.y); // XZ offset
        Vector3 randomDropTargetPos = robotBaseReference.TransformPoint(dropOffsetFromBase); // Convert local offset to world
        randomDropTargetPos.y = robotBaseReference.position.y + dropTargetYRelativeToBase; // Set Y relative to base
        Quaternion dropOrientation = CalculateGripperOrientation(randomDropTargetPos);

        // (Optional) Move to a pre-drop approach position if desired (e.g., a fixed point high above the drop area)
        // For simplicity, we'll go straight to the calculated drop approach.
        Vector3 dropApproachPos = new Vector3(randomDropTargetPos.x, randomDropTargetPos.y + approachLiftVerticalOffset, randomDropTargetPos.z);

        currentSequenceState = SequenceState.MovingToDropApproach;
        Debug.Log("State: Moving to Drop Approach");
        yield return StartCoroutine(MoveAndOrientTarget(dropApproachPos, dropOrientation));


        currentSequenceState = SequenceState.MovingToDrop;
        Debug.Log("State: Moving to Drop");
        yield return StartCoroutine(MoveAndOrientTarget(randomDropTargetPos, dropOrientation));

        currentSequenceState = SequenceState.Releasing;
        Debug.Log("State: Releasing");
        gripper.Grip(false); // This should unparent the dice
        // Ensure dice is ready for physics:
        if (diceTransform.parent == gripper.transform) diceTransform.SetParent(null); // Force unparent if Flange Gripper didn't
        diceRigidbody.isKinematic = false;
        diceRigidbody.useGravity = true;
        diceRigidbody.velocity = Vector3.zero; // Reset velocity before rolling
        diceRigidbody.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(gripOperationTime);


        currentSequenceState = SequenceState.LiftingFromDrop;
        Debug.Log("State: Lifting from Drop");
        Vector3 liftFromDropPos = new Vector3(randomDropTargetPos.x, randomDropTargetPos.y + approachLiftVerticalOffset, randomDropTargetPos.z);
        yield return StartCoroutine(MoveAndOrientTarget(liftFromDropPos, dropOrientation));

        currentSequenceState = SequenceState.Rolling;
        Debug.Log("State: Rolling Dice");
        if (diceRigidbody != null && !diceRigidbody.isKinematic)
        {
            diceRigidbody.WakeUp(); // Make sure it's active
            Vector3 forceDirection = Random.onUnitSphere;
            forceDirection.y = Mathf.Max(0.1f, Mathf.Abs(forceDirection.y * 0.5f)); // More horizontal/upward push
            diceRigidbody.AddForce(forceDirection.normalized * diceRollForce, ForceMode.Impulse);
            diceRigidbody.AddTorque(Random.insideUnitSphere * diceTorqueForce, ForceMode.Impulse);
        }
        yield return new WaitForSeconds(diceSettleTime);

        // 3. (Optional) Return Home
        if (homePositionMarker != null && returnHome)
        {
            currentSequenceState = SequenceState.ReturningHome;
            Debug.Log("State: Returning Home");
            Vector3 homePos = homePositionMarker.position;
            Quaternion homeOrient = CalculateGripperOrientation(homePos); // Or use homePositionMarker.rotation
            yield return StartCoroutine(MoveAndOrientTarget(homePos, homeOrient));
        }

        Debug.Log("Sequence Complete. Idle.");
        currentSequenceState = SequenceState.Idle;
        activeSequenceCoroutine = null;
    }
}