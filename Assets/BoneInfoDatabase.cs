using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BoneInfoCollection
{
    public BoneInfo[] bones;
}

[Serializable]
public class BoneInfo
{
    public int no;
    public string objectBlender;
    public string namaTampilanIndonesia;
    public string namaAnatomiLatin;
    public string deskripsi;
    public string faktaAnatomiSederhana;
}

public sealed class BoneInfoDatabase
{
    private const string ResourceName = "bone_object_info";

    private readonly Dictionary<string, BoneInfo> bonesByObjectName;
    private readonly List<BoneInfo> bones;

    private BoneInfoDatabase(List<BoneInfo> bones)
    {
        this.bones = bones;
        bonesByObjectName = new Dictionary<string, BoneInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (BoneInfo bone in bones)
        {
            if (bone == null || string.IsNullOrWhiteSpace(bone.objectBlender))
            {
                continue;
            }

            bonesByObjectName[bone.objectBlender] = bone;
        }
    }

    public IReadOnlyList<BoneInfo> Bones
    {
        get { return bones; }
    }

    public static BoneInfoDatabase Load()
    {
        TextAsset json = Resources.Load<TextAsset>(ResourceName);
        if (json == null)
        {
            Debug.LogWarning("BoneInfoDatabase: Assets/Resources/bone_object_info.json was not found.");
            return new BoneInfoDatabase(new List<BoneInfo>());
        }

        BoneInfoCollection collection = JsonUtility.FromJson<BoneInfoCollection>(json.text);
        List<BoneInfo> loadedBones = collection != null && collection.bones != null
            ? new List<BoneInfo>(collection.bones)
            : new List<BoneInfo>();

        loadedBones.Sort((left, right) => left.no.CompareTo(right.no));
        return new BoneInfoDatabase(loadedBones);
    }

    public bool TryGet(string objectName, out BoneInfo boneInfo)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            boneInfo = null;
            return false;
        }

        return bonesByObjectName.TryGetValue(objectName, out boneInfo);
    }
}
