using System.ComponentModel.Design.Serialization;
using UnityEngine;
using UnityEngine.UIElements;

public class UISCRIPTBASE : MonoBehaviour
{
    public GameObject GameObject;
    private Button jnrm;
    private Button crrm;
    void OnEnable()
    {
        var root=GameObject.GetComponent<UIDocument>().rootVisualElement;
        jnrm = root.Q<Button>("joinButton");
        crrm = root.Q<Button>("createButton");
        if ( crrm != null)
        {
            crrm.clicked += OnCreate;
        }
        if (jnrm != null)
        {
            jnrm.clicked += OnJoin;
        }
    }


    void OnDisable()
    {
        if ( crrm != null)
        {
            crrm.clicked -= OnCreate;
        }
        if (jnrm != null)
        {
            jnrm.clicked -= OnJoin;
        }
    }
    void OnJoin()
    {
        
    }
    void OnCreate()
    {
        
    }
}
