﻿using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace Klonamari
{
    public class Katamari : MonoBehaviour
    {
        private const float ONE_THIRD = 1.0f / 3.0f;

        public float ROLL_UP_MAX_RATIO = 0.25f; //NOTE that this isn't talking about rolling up stairs. the game's lingo uses this for collection.

        public float TORQUE_MULT = 1500.0f;
        public float FORCE_MULT = 500.0f;
        public float AIRBORNE_FORCE_MULT = 250.0f;
        public float UPWARD_FORCE_MULT = 1000.0f;
        public float STAIR_CLIMB_RATIO = 2.15f; // you can climb sheer walls STAIR_CLIMB_RATIO * radius above initial contact. if it's taller than that, you're falling down.

        private KatamariInput katamariInput;
        public FollowKatamari follow;

        public Rigidbody rB;
        public SphereCollider sphere;
        private float volume;
        public float density;
        public float mass { get; private set; }

        private List<Transform> touchingClimbables = new List<Transform>();

        public bool isGrounded;
        public int defaultContacts
        {
            get; private set;
        }

        private List<CollectibleObject> collectibles = new List<CollectibleObject>();
        private List<CollectibleObject> irregularCollectibles = new List<CollectibleObject>(); //TODO, maybe use a Dictionary. or sort this list as things are inserted.
        
        void OnEnable()
        {
            //A note here, I'd rather pull all of this stuff up into a Context class and keep all platform dependent compilation up there.
            //the Context class could fill out either a Service Locator or set up bindings for DI. This class would just ask for an instance
            //of KatamariInput from injection or from the locator instead of calling new here.
#if UNITY_EDITOR || UNITY_STANDALONE
            SetInput(new KatamariKeyboardInput());
#elif UNITY_XBOX360 || UNITY_XBOXONE
            SetInput(new KatamariJoystickInput());
#endif
            //TODO: other input implementations for mobile, joystick, eye tracking, etc. we could also build a way for the user to select them once we have more.

            KatamariEventManager.OnInputChanged += SetInput;
        }

        void OnDisable()
        {
            KatamariEventManager.OnInputChanged -= SetInput;
        }

        void Start()
        {
            rB = GetComponent<Rigidbody>();
            sphere = GetComponent<SphereCollider>();
            volume = 4.0f / 3.0f * Mathf.PI * Mathf.Pow(sphere.radius, 3); //initial volume calculated by radius of the sphere.
            rB.mass = mass = density * volume;
        }

        private void SetInput(KatamariInput input)
        {
            katamariInput = input;
        }

        private void ProcessInput(Vector3 input)
        {
            float forwardInputMultiplier = input.z * Time.deltaTime;
            float lateralInputMultiplier = input.x * Time.deltaTime;
            float upwardInputMultiplier = 0.0f;

            if ((Mathf.Abs(forwardInputMultiplier) > float.Epsilon || Mathf.Abs(lateralInputMultiplier) > float.Epsilon) && defaultContacts > 0)
            {
                //Debug.Log("up");
                upwardInputMultiplier += Time.deltaTime * UPWARD_FORCE_MULT; //* 1.0f, you know.
            }

            float adjustedTorqueMultiplier = TORQUE_MULT * rB.mass;
            float adjustedForceMultiplier = rB.mass;
            if (!isGrounded)
            {
                adjustedForceMultiplier *= AIRBORNE_FORCE_MULT;
            }
            else
            {
                adjustedForceMultiplier *= FORCE_MULT;
            }

            rB.AddTorque(forwardInputMultiplier * adjustedTorqueMultiplier, input.y * adjustedTorqueMultiplier * Time.deltaTime, -lateralInputMultiplier * adjustedTorqueMultiplier);
            rB.AddForce(lateralInputMultiplier * adjustedForceMultiplier, upwardInputMultiplier, forwardInputMultiplier * adjustedForceMultiplier);
        }

        // Update is called once per frame
        void Update()
        {
            isGrounded = Physics.Raycast(transform.position, Vector3.down, sphere.radius + 0.01f);

            Vector3 input = katamariInput.Update(this);
            ProcessInput(input);


            //TODO: let's do something with the awful camera. it needs to rotate about our y axis when we turn. forces need to be applied from its perspective.



            follow.UpdatePosition(this);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.layer == 10)
            {
                return;
            }

            bool rolledUp = OnContact(collision);

            if (!rolledUp)
            {
                Collider hit = collision.rigidbody.GetComponent<Collider>();
                float targetTop = hit.bounds.extents.y + collision.transform.position.y;
                float sphereBottom = transform.position.y - sphere.radius;
                if (collision.gameObject.layer == 8 && targetTop > sphereBottom && sphereBottom + STAIR_CLIMB_RATIO * sphere.radius > targetTop) //allowing a little cheat on sphere radius
                {
                    ++defaultContacts;
                    touchingClimbables.Add(collision.transform);
                    //Debug.Log("default contacts: " + defaultContacts);
                }
            }
        }

        void OnCollisionExit(Collision collision)
        {
            //Debug.Log("exit");
            if (touchingClimbables.Contains(collision.transform))
            {
                --defaultContacts;
                touchingClimbables.Remove(collision.transform);
                //Debug.Log("default contacts: " + defaultContacts);
            }
        }

        private bool OnContact(Collision collision)
        {
            bool rolledUp = false;
            Transform t = collision.transform;
            
            //Debug.Log("hit. v: " + (collision.relativeVelocity.magnitude) + ", layer: " + collision.gameObject.layer);

            CollectibleObject collectible = t.GetComponent<CollectibleObject>();
            if (collectible)
            {
                if (collectible.mass < mass * ROLL_UP_MAX_RATIO)
                {
                    if (!collectible.collected)
                    {
                        //TODO: we should update our model, I guess. mass and uhh...diameter? changed. notify that we collected the new object
                        //Debug.Log("attach");

                        collectible.collected = true;
                        rolledUp = true;
                        collectible.gameObject.layer = 9;

                        t.parent = transform;

                        volume += collectible.volume;
                        mass += collectible.mass;
                        RecalculateRadius();
                        collectibles.Add(collectible);

                        Vector3 delta = (collectible.transform.position - transform.position);
                        float distance = delta.magnitude - sphere.radius;
                        Vector3 direction = delta.normalized;
                        collectible.transform.position = collectible.transform.position - direction * distance;

                        KatamariEventManager.Attach(collectible);

                        if (collectible.IsIrregular(sphere.radius)) //irregular objects will modify how our controls work. it might actually need to be a function of scale compared to our radius.
                        {
                            Destroy(collectible.rB);
                            irregularCollectibles.Add(collectible);
                        }
                        else {
                            collectible.rB.detectCollisions = false;
                            collectible.rB.isKinematic = true;
                        }
                    }
                }
                else
                {
                    float magnitude = collision.relativeVelocity.magnitude;
                    while (magnitude >= 7.0f && collectibles.Count > 0)
                    {
                        CollectibleObject toRemove = collectibles[collectibles.Count - 1];
                        collectibles.RemoveAt(collectibles.Count - 1);

                        OnDetach(toRemove);
                        magnitude -= 4.0f;
                    }
                }
            }
            return rolledUp;
        }

        void OnDetach(CollectibleObject detached)
        {
            //Debug.Log("detach");
            volume -= detached.volume;
            RecalculateRadius();
            mass -= detached.mass;

            detached.Detach(this);

            KatamariEventManager.Detach(detached);
            //TODO: we should update our model, I guess. mass and uhh...diameter? changed.
        }

        private void RecalculateRadius()
        {
            sphere.radius = Mathf.Pow((3 * volume) / (4 * Mathf.PI), ONE_THIRD);
            int irregulars = irregularCollectibles.Count;
            for (int i = irregulars - 1; i >= 0; --i)
            {
                CollectibleObject collectible = irregularCollectibles[i];
                if (!collectible.IsIrregular(sphere.radius))
                {
                    irregularCollectibles.RemoveAt(i);

                    collectible.rB = collectible.gameObject.AddComponent<Rigidbody>();
                    collectible.rB.detectCollisions = false;
                    collectible.rB.isKinematic = true;
                    collectible.rB.mass = collectible.volume * collectible.density;
                }
            }

            //TODO: let's see if we should combine some meshes.
            int collectibleCount = collectibles.Count;
            if (collectibleCount >= 40)
            {
                CombineCollectibles();
            }
        }

        private void CombineCollectibles()
        {
            Debug.Log("combine");
            /*int collectibleCount = collectibles.Count;
            for (int i = collectibleCount - 1; i <= 0; --i)
            {
                CollectibleObject collectible = collectibles[i];
                collectibles.RemoveAt(i);

                
            }*/

            //TODO: combine?
            //GetComponent<Mesh>().CombineMeshes()
        }
    }
}