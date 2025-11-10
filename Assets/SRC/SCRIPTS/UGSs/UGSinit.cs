using System;
using Unity.Services.Core;
using UnityEngine;

public class UDGinit : MonoBehaviour
{
	private UDGinit instance;
	void Awake()
    {
		if (instance != null)
		{
			Destroy(gameObject);
		}
		instance = this;
    }
	async void Start()
	{
		try
		{
			await UnityServices.InitializeAsync();
			Debug.Log("UGS SCRIPT RUN");
		}
		catch (Exception e)
		{
			Debug.LogException(e);
		}
		DontDestroyOnLoad(gameObject);
	}
}