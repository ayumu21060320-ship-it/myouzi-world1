using System.Collections.Generic;
using UnityEngine;

// パリコレモデルの体（ぐにゃぐにゃの人形）。横から見て、右（+x）へ歩く。
// 体の部品は Rigidbody2D で、関節は HingeJoint2D でつなぐだけ。関節を動かす力は、この中で毎回計算して加える
// （HingeJoint2D のモーターや角度の制限は向きの決まりがわかりにくいので使わない）。
//
// 操作（RunwayGame から毎回渡す）:
//   左足 前へ・後ろへ / 右足 前へ・後ろへ … 太ももを付け根から回す力（押しているあいだだけ）
//   ひざを伸ばす ……………………………………… 押しているあいだ、両ひざがまっすぐになろうとする。離すとひざが曲がる
// わざと難しくするため、体をまっすぐに保つ手助けには上限がある（キーをでたらめに押すと必ず転ぶ）。
public class RunwayModel
{
    // ---- 調整用の数（遊んでみて変える）----
    public static float HipTorque = 35f;        // 太ももを回す力
    public static float HipDamping = 0.6f;      // 太ももの回りすぎを止める力
    public static float KneeStiffStraight = 4f; // ひざを伸ばすときの強さ（1 度ずれるごとの力）
    public static float KneeStiffLoose = 0.6f;  // ひざを伸ばしていないときの強さ
    public static float KneeLooseAngle = -35f;  // 力を抜いたときのひざの角度（マイナス = 普通の向きに曲がる）
    public static float AnkleStiff = 1.2f;      // 足首をまっすぐに保つ強さ
    public static float UprightAssist = 800f;   // 体をまっすぐに保つ手助け（0 にすると、とても難しい）
    public static float UprightMax = 30f;       // 手助けの力の上限（小さなぐらつきは直すが、大きく傾くと助けきれずに転ぶ）

    // 物理の 1 回の計算の時間（秒）。関節のばねが強すぎると体がふるえて飛び散るので、この時間に合わせて強さに上限をつける
    public static float StepTime = 0.0025f;

    // 寸法（メートルのつもり）。地面の上が y = 0
    const float HeelHeight = 0.10f, FootLength = 0.22f;
    const float ShinLength = 0.45f, ThighLength = 0.45f;
    const float TorsoLength = 0.58f, TorsoWidth = 0.26f;
    const float HeadRadius = 0.12f;
    const float UpperArmLength = 0.30f, ForearmLength = 0.28f;
    const float LimbWidth = 0.09f;

    public Rigidbody2D Torso, Head;
    public Collider2D TorsoCollider, HeadCollider;
    readonly Leg left, right;
    readonly Rigidbody2D[] arms = new Rigidbody2D[4];
    readonly List<GameObject> parts = new List<GameObject>();
    readonly List<Collider2D> colliders = new List<Collider2D>();

    class Leg
    {
        public Rigidbody2D thigh, shin, foot;
    }

    // 入力
    public bool LeftForward, LeftBack, RightForward, RightBack, KneesStraight;

    static readonly Color Skin = new Color(0.96f, 0.82f, 0.7f);
    static readonly Color SkinBack = new Color(0.8f, 0.66f, 0.56f);   // 奥の手足（少し暗く）
    static readonly Color Dress = new Color(0.1f, 0.1f, 0.12f);
    static readonly Color Shoe = new Color(0.85f, 0.1f, 0.15f);
    static readonly Color ShoeBack = new Color(0.6f, 0.07f, 0.1f);
    static readonly Color Hair = new Color(0.2f, 0.12f, 0.08f);

    public RunwayModel(Vector2 start, Sprite square, Sprite circle, PhysicsMaterial2D grip, PhysicsMaterial2D slip)
    {
        float ankleY = HeelHeight + 0.04f;
        float kneeY = ankleY + ShinLength;
        float hipY = kneeY + ThighLength;
        float neckY = hipY + TorsoLength;

        // 胴体（ドレス）と頭
        Torso = MakeBox("Torso", start + new Vector2(0, hipY + TorsoLength / 2), new Vector2(TorsoWidth, TorsoLength), 12f, slip,
                        square, Dress, 5);
        TorsoCollider = Torso.GetComponent<Collider2D>();
        AddVisual(Torso.transform, square, Dress, new Vector2(0, -TorsoLength / 2 + 0.06f), new Vector2(TorsoWidth + 0.14f, 0.16f), 5); // スカートのすそ
        Head = MakeCircle("Head", start + new Vector2(0, neckY + HeadRadius + 0.02f), HeadRadius, 3f, slip, circle, Skin, 7);
        HeadCollider = Head.GetComponent<Collider2D>();
        AddVisual(Head.transform, circle, Hair, new Vector2(-0.03f, 0.04f), new Vector2(HeadRadius * 2.1f, HeadRadius * 1.6f), 8); // 髪
        Pin(Head, Torso, start + new Vector2(0, neckY));

        // 足（奥が左足、手前が右足）
        left = MakeLeg("Left", start, ankleY, kneeY, hipY, square, grip, slip, SkinBack, ShoeBack, 1);
        right = MakeLeg("Right", start, ankleY, kneeY, hipY, square, grip, slip, Skin, Shoe, 9);

        // 腕（ぶらぶら。操作できない）
        for (int side = 0; side < 2; side++)
        {
            bool back = side == 0;
            Color c = back ? SkinBack : Skin;
            int order = back ? 0 : 10;
            float shoulderY = neckY - 0.04f;
            var upper = MakeBox("UpperArm", start + new Vector2(0, shoulderY - UpperArmLength / 2), new Vector2(LimbWidth * 0.8f, UpperArmLength), 1.2f,
                                slip, square, c, order);
            var fore = MakeBox("Forearm", start + new Vector2(0, shoulderY - UpperArmLength - ForearmLength / 2), new Vector2(LimbWidth * 0.7f, ForearmLength), 0.8f,
                               slip, square, c, order);
            Pin(upper, Torso, start + new Vector2(0, shoulderY));
            Pin(fore, upper, start + new Vector2(0, shoulderY - UpperArmLength));
            arms[side * 2] = upper;
            arms[side * 2 + 1] = fore;
        }

        // 体の部品どうしはぶつからない
        for (int i = 0; i < colliders.Count; i++)
            for (int j = i + 1; j < colliders.Count; j++)
                Physics2D.IgnoreCollision(colliders[i], colliders[j]);
    }

    Leg MakeLeg(string name, Vector2 start, float ankleY, float kneeY, float hipY, Sprite square,
                PhysicsMaterial2D grip, PhysicsMaterial2D slip, Color skin, Color shoe, int order)
    {
        var leg = new Leg();
        leg.thigh = MakeBox(name + "Thigh", start + new Vector2(0, kneeY + ThighLength / 2), new Vector2(LimbWidth * 1.15f, ThighLength), 5f,
                            slip, square, skin, order);
        leg.shin = MakeBox(name + "Shin", start + new Vector2(0, ankleY + ShinLength / 2), new Vector2(LimbWidth, ShinLength), 3f,
                           slip, square, skin, order);

        // ハイヒール: 足の裏の板と、かかとの細い棒と、つま先の小さな台。地面に着くのは細いところだけなので、ぐらつく
        var footGo = new GameObject(name + "Foot");
        footGo.transform.position = start + new Vector2(0.04f, HeelHeight + 0.02f);
        var foot = footGo.AddComponent<Rigidbody2D>();
        foot.mass = 1f;
        var plate = footGo.AddComponent<BoxCollider2D>();
        plate.size = new Vector2(FootLength, 0.03f);
        plate.sharedMaterial = slip;
        var heel = footGo.AddComponent<BoxCollider2D>();
        heel.size = new Vector2(0.02f, HeelHeight);
        heel.offset = new Vector2(-FootLength / 2 + 0.015f, -HeelHeight / 2);
        heel.sharedMaterial = grip;
        var toe = footGo.AddComponent<BoxCollider2D>();
        toe.size = new Vector2(0.05f, 0.03f);
        toe.offset = new Vector2(FootLength / 2 - 0.025f, -HeelHeight + 0.015f);
        toe.sharedMaterial = grip;
        colliders.Add(plate);
        colliders.Add(heel);
        colliders.Add(toe);
        AddVisual(footGo.transform, square, shoe, Vector2.zero, plate.size, order);
        AddVisual(footGo.transform, square, shoe, heel.offset, heel.size, order);
        AddVisual(footGo.transform, square, shoe, toe.offset, toe.size, order);
        parts.Add(footGo);
        leg.foot = foot;

        Pin(leg.thigh, Torso, start + new Vector2(0, hipY));
        Pin(leg.shin, leg.thigh, start + new Vector2(0, kneeY));
        Pin(leg.foot, leg.shin, start + new Vector2(0, ankleY));
        return leg;
    }

    // 毎回の物理の計算の前に呼ぶ: 関節を動かす力を加える
    public void ApplyControls()
    {
        DriveLeg(left, LeftForward, LeftBack);
        DriveLeg(right, RightForward, RightBack);

        // 首: まっすぐに戻ろうとする（ゆるく）
        Spring(Torso, Head, 0f, 0.4f, 0.05f, -30f, 30f);

        // 腕: ぶらぶら。曲がりすぎだけ止める
        for (int side = 0; side < 2; side++)
        {
            Spring(Torso, arms[side * 2], 0f, 0.02f, 0.01f, -100f, 100f);
            Spring(arms[side * 2], arms[side * 2 + 1], 10f, 0.02f, 0.01f, 0f, 130f);
        }

        // 体をまっすぐに保つ手助け（ほんの少し）
        float tilt = Mathf.DeltaAngle(0f, Torso.rotation);
        float assist = -tilt * UprightAssist * 0.01f - Torso.angularVelocity * 0.002f * UprightAssist;
        Torso.AddTorque(Mathf.Clamp(assist, -UprightMax, UprightMax));
    }

    void DriveLeg(Leg leg, bool forward, bool back)
    {
        // 太もも（前へ回す = 反時計回り = プラス）
        float hipTorque = (forward ? HipTorque : 0f) - (back ? HipTorque : 0f);
        float relVel = leg.thigh.angularVelocity - Torso.angularVelocity;
        hipTorque -= relVel * HipDamping * 0.05f;
        leg.thigh.AddTorque(hipTorque);
        Torso.AddTorque(-hipTorque);
        SoftLimit(Torso, leg.thigh, -110f, 80f);

        // ひざ（曲がる向きはマイナス。前には曲がらない）
        float kneeTarget = KneesStraight ? 0f : KneeLooseAngle;
        float kneeStiff = KneesStraight ? KneeStiffStraight : KneeStiffLoose;
        Spring(leg.thigh, leg.shin, kneeTarget, kneeStiff, kneeStiff * 0.04f, -140f, 2f);

        // 足首
        Spring(leg.shin, leg.foot, 0f, AnkleStiff, AnkleStiff * 0.04f, -35f, 35f);
    }

    // 子の部品を、親から見た角度 target に近づける力（ばね）。lo〜hi を超えたら強く押し戻す。
    // stiff = 1 度ずれるごとの力、damp = 1 秒に 1 度動くごとのブレーキ
    static void Spring(Rigidbody2D parent, Rigidbody2D child, float target, float stiff, float damp, float lo, float hi)
    {
        float rel = Mathf.DeltaAngle(parent.rotation, child.rotation);
        float relVel = child.angularVelocity - parent.angularVelocity;
        Cap(parent, child, ref stiff, ref damp);
        float torque = (target - rel) * stiff - relVel * damp;
        child.AddTorque(torque);
        parent.AddTorque(-torque);
        SoftLimit(parent, child, lo, hi);
    }

    static void SoftLimit(Rigidbody2D parent, Rigidbody2D child, float lo, float hi)
    {
        float rel = Mathf.DeltaAngle(parent.rotation, child.rotation);
        float over = rel > hi ? hi - rel : rel < lo ? lo - rel : 0f;
        if (over == 0f) return;
        float relVel = child.angularVelocity - parent.angularVelocity;
        float stiff = 8f, damp = 0.3f;
        Cap(parent, child, ref stiff, ref damp);
        float torque = over * stiff - relVel * damp;
        child.AddTorque(torque);
        parent.AddTorque(-torque);
    }

    // ばねの強さを、ふるえない範囲までにおさえる（軽い部品どうしほど弱く）
    static void Cap(Rigidbody2D a, Rigidbody2D b, ref float stiff, ref float damp)
    {
        float ia = a.inertia, ib = b.inertia;
        float inertia = ia * ib / Mathf.Max(1e-6f, ia + ib);
        float h = StepTime;
        stiff = Mathf.Min(stiff, 0.3f * inertia / (h * h) * Mathf.Deg2Rad);
        damp = Mathf.Min(damp, 0.5f * inertia / h * Mathf.Deg2Rad);
    }

    // 頭の高さ・体の傾き（点数と転んだかどうかに使う）
    public float HeadHeight => Head.position.y;
    public float Tilt => Mathf.DeltaAngle(0f, Torso.rotation);
    public float X => Torso.position.x;

    public void Destroy()
    {
        foreach (var p in parts) Object.Destroy(p);
        parts.Clear();
    }

    // ---- 部品を作る ----

    Rigidbody2D MakeBox(string name, Vector2 pos, Vector2 size, float mass, PhysicsMaterial2D mat, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var rb = go.AddComponent<Rigidbody2D>();
        rb.mass = mass;
        var col = go.AddComponent<BoxCollider2D>();
        col.size = size;
        col.sharedMaterial = mat;
        colliders.Add(col);
        AddVisual(go.transform, sprite, color, Vector2.zero, size, order);
        parts.Add(go);
        return rb;
    }

    Rigidbody2D MakeCircle(string name, Vector2 pos, float radius, float mass, PhysicsMaterial2D mat, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        var rb = go.AddComponent<Rigidbody2D>();
        rb.mass = mass;
        var col = go.AddComponent<CircleCollider2D>();
        col.radius = radius;
        col.sharedMaterial = mat;
        colliders.Add(col);
        AddVisual(go.transform, sprite, color, Vector2.zero, new Vector2(radius * 2, radius * 2), order);
        parts.Add(go);
        return rb;
    }

    // 見た目（色の付いた四角や丸）。物理の部品の子にして、一緒に動かす
    public static SpriteRenderer AddVisual(Transform parent, Sprite sprite, Color color, Vector2 offset, Vector2 size, int order)
    {
        var go = new GameObject("Visual");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = offset;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    // 2 つの部品を、point（世界の位置）でピンのようにつなぐ
    static void Pin(Rigidbody2D child, Rigidbody2D parent, Vector2 point)
    {
        var j = child.gameObject.AddComponent<HingeJoint2D>();
        j.connectedBody = parent;
        j.autoConfigureConnectedAnchor = true;
        j.anchor = child.transform.InverseTransformPoint(point);
        j.enableCollision = false;
    }
}
