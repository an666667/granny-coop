using UnityEngine;

public class Door : MonoBehaviour {

    [Header("Door settings")]
    public Animation m_doorAnimation;
    public string m_openDoorAnimationName;
    public string m_closeDoorAnimationName;
    bool m_isOpen;
    bool m_netGuard;

    [Header("Door Sounds settings")]
    [Tooltip("Door opening sound")]
    public AudioClip openSound;
    [Tooltip("Door closing sound")]
    public AudioClip closeSound;
    [Tooltip("Door unlocking sound")]
    public AudioClip unlockSound;


    public void Interact()
    {
        if(!m_doorAnimation.isPlaying)
        {
            if(!m_isOpen)
            {
                m_isOpen = true;
                m_doorAnimation.Play(m_openDoorAnimationName);
                AudioSource.PlayClipAtPoint(openSound,transform.position);
            }else
            {
                m_isOpen = false;
                m_doorAnimation.Play(m_closeDoorAnimationName);
                AudioSource.PlayClipAtPoint(closeSound, transform.position);
            }
            if (!m_netGuard && GrannyCoop.CoopSession.IsCoop)
                GrannyCoop.CoopSession.Instance.BroadcastDoor(transform.position, m_isOpen ? 1 : 0, GrannyCoop.CoopSession.PathId(transform));
        }
    }

    /// <summary>Apply a door state received from the peer (no re-broadcast).</summary>
    public void NetApply(bool open)
    {
        if (m_isOpen == open) return;
        m_netGuard = true;
        m_isOpen = open;
        if (open)
        {
            m_doorAnimation.Play(m_openDoorAnimationName);
            AudioSource.PlayClipAtPoint(openSound, transform.position);
        }
        else
        {
            m_doorAnimation.Play(m_closeDoorAnimationName);
            AudioSource.PlayClipAtPoint(closeSound, transform.position);
        }
        m_netGuard = false;
    }


}
