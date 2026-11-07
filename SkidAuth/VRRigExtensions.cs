namespace SkidAuth
{
	public static class VRRigExtensions
	{
		public static NetPlayer GetPlayer(this VRRig rig)
		{
			NetPlayer creator = rig.Creator;
			if (creator == null)
			{
				creator = NetworkSystem.Instance.GetPlayer(rig.Creator?.GetPlayerRef().ActorNumber ?? -1);
			}
			return creator;
		}

		public static bool IsTagged(this VRRig rig)
		{
			return rig != null && GameModeUtilities.InfectedList().Contains(rig.GetPlayer());
		}
	}
}