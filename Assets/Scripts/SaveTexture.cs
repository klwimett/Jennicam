using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

[ExecuteInEditMode]
public class SaveTexture : MonoBehaviour {
	public bool save;
	public Texture2D  texture;
	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
		texture = GetComponent<Renderer>().material.mainTexture as Texture2D;

		if(!save) return;

		save = false;
		
		var jpg = texture.EncodeToJPG();

		File.WriteAllBytes(Application.dataPath+"/"+gameObject.name+".jpg",jpg);

		Debug.Log("Finished!");
	}
}
