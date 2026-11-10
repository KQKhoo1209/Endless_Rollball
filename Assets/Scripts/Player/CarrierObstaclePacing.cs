using System.Collections.Generic;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball
{
    // Seeded, local event choices. No global Random state or startup checks.
    internal static class CarrierObstaclePacing
    {
        internal static Vector2 Interval(DifficultyMode mode,float progression,bool lava)
        {
            Vector2 easy=lava?new Vector2(4.5f,5.5f):new Vector2(4f,5f);
            Vector2 normal=lava?new Vector2(3.5f,4.5f):new Vector2(3f,4f);
            Vector2 hard=lava?new Vector2(2.8f,3.6f):new Vector2(2.2f,3f);
            return mode==DifficultyMode.Easy?easy:mode==DifficultyMode.Normal?normal:
                mode==DifficultyMode.Hard?hard:Vector2.Lerp(easy,hard,Mathf.Clamp01(progression));
        }

        internal static float Range(System.Random random,Vector2 range) =>
            Mathf.Lerp(range.x,range.y,(float)random.NextDouble());

        internal static int Draw(List<int> bag,int size,System.Random random,int previous)
        {
            if(bag.Count==0)
            {
                for(int i=0;i<size;i++) bag.Add(i);
                for(int i=bag.Count-1;i>0;i--)
                {int j=random.Next(i+1);int value=bag[i];bag[i]=bag[j];bag[j]=value;}
            }
            int selected=bag.Count-1;
            if(bag[selected]==previous)
                for(int i=selected-1;i>=0;i--)
                    if(bag[i]!=previous) {selected=i;break;}
            int result=bag[selected];bag.RemoveAt(selected);return result;
        }

        internal static Bounds Footprint(Component source)
        {
            Bounds result=default;bool found=false;
            foreach(var shape in source.GetComponentsInChildren<Collider>(true))
            {
                Vector3 centre,size;
                if(shape is BoxCollider box) {centre=box.center;size=box.size;}
                else if(shape is SphereCollider sphere) {centre=sphere.center;size=Vector3.one*sphere.radius*2;}
                else if(shape is CapsuleCollider capsule)
                {
                    centre=capsule.center;size=Vector3.one*capsule.radius*2;
                    size[capsule.direction]=Mathf.Max(capsule.height,size[capsule.direction]);
                }
                else continue;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 point=centre+Vector3.Scale(size*.5f,new Vector3(
                        (corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    point=shape.transform.TransformPoint(point)-source.transform.position;
                    if(found) result.Encapsulate(point);else {result=new Bounds(point,Vector3.zero);found=true;}
                }
            }
            return found?result:new Bounds(Vector3.zero,new Vector3(2,2,3));
        }

        internal static float Clearance(float previousDepth,float depth,Vector3 carrier,
            float previousPassage,float passage,float speed,float steeringSpeed,float acceleration)
        {
            return (previousDepth+depth+2*carrier.z+.5f)/Mathf.Max(.1f,speed)+
                EncounterPlanner.SteeringTime(previousPassage,passage,steeringSpeed,acceleration)+2*Time.fixedDeltaTime;
        }

        internal static bool TryPassage(Bounds bounds,float x,float width,Vector3 carrier,float preferred,out float passage)
        {
            float low=-width*.5f+carrier.x+.2f,high=-low;
            float leftHigh=Mathf.Min(high,x+bounds.min.x-carrier.x-.2f);
            float rightLow=Mathf.Max(low,x+bounds.max.x+carrier.x+.2f);
            bool left=leftHigh>=low,right=rightLow<=high;
            passage=0;
            if(!left && !right) return false;
            float a=Mathf.Clamp(preferred,low,Mathf.Max(low,leftHigh));
            float b=Mathf.Clamp(preferred,Mathf.Min(high,rightLow),high);
            passage=!right || (left && Mathf.Abs(a-preferred)<Mathf.Abs(b-preferred))?a:b;
            return true;
        }
    }
}
