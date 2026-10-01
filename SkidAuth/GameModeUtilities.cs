using System.Collections.Generic;
using GorillaGameModes;
using Photon.Pun;

namespace SkidAuth
{
	public static class GameModeUtilities
	{
		public static List<NetPlayer> InfectedList()
		{
			var infected = new List<NetPlayer>();
			if (!PhotonNetwork.InRoom || GorillaGameManager.instance == null)
			{
				return infected;
			}

			bool isTagMode = GorillaGameManager.instance.GameType() is GameModeType.Infection
				or GameModeType.FreezeTag
				or GameModeType.PropHunt
				or GameModeType.InfectionCompetitive;

			if (isTagMode && GorillaGameManager.instance is GorillaTagManager tagManager)
			{
				if (tagManager.isCurrentlyTag && tagManager.currentIt != null)
				{
					infected.Add(tagManager.currentIt);
				}
				else
				{
					infected.AddRange(tagManager.currentInfected);
				}
			}

			return infected;
		}
	}
}