using UnityEngine;

namespace SkidAuth
{
	public sealed class BillboardToCamera : MonoBehaviour
	{
		private void LateUpdate()
		{
			Camera main = Camera.main;
			if (main == null) return;

			transform.LookAt(
				transform.position + main.transform.rotation * Vector3.forward,
				main.transform.rotation * Vector3.up);
		}
	}
}