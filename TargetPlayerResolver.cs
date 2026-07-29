using System;
using System.Reflection;
using EntityComponent;
using JumpKing.Player;

namespace SuperSaiyan
{
    internal static class TargetPlayerResolver
    {
        private const string ApiTypeName =
            "LocalMultiplayerMod.LocalMultiplayerApi";

        private delegate int ResolvePlayerMaskDelegate(string user);
        private delegate PlayerEntity GetPlayerDelegate(int playerNumber);
        private delegate int GetCurrentViewPlayerMaskDelegate();

        private static bool _resolved;
        private static ResolvePlayerMaskDelegate _resolvePlayerMask;
        private static GetPlayerDelegate _getPlayer;
        private static GetCurrentViewPlayerMaskDelegate _getCurrentViewPlayerMask;

        public static void ResolveApi()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type apiType = assemblies[i].GetType(ApiTypeName, false);
                if (apiType == null)
                {
                    continue;
                }

                _resolvePlayerMask = CreateDelegate<ResolvePlayerMaskDelegate>(
                    apiType,
                    "ResolvePlayerMask"
                );
                _getPlayer = CreateDelegate<GetPlayerDelegate>(apiType, "GetPlayer");
                _getCurrentViewPlayerMask =
                    CreateDelegate<GetCurrentViewPlayerMaskDelegate>(
                        apiType,
                        "GetCurrentViewPlayerMask"
                    );
                return;
            }
        }

        public static int ResolvePlayerMask(string user)
        {
            ResolveApi();
            return _resolvePlayerMask == null ? 1 : _resolvePlayerMask(user);
        }

        public static PlayerEntity GetPlayer(int playerNumber)
        {
            ResolveApi();
            if (_getPlayer != null)
            {
                return _getPlayer(playerNumber);
            }

            return playerNumber == 1 && EntityManager.instance != null ?
                EntityManager.instance.Find<PlayerEntity>() : null;
        }

        public static bool IsPlayerInCurrentView(int playerNumber)
        {
            ResolveApi();
            int mask = _getCurrentViewPlayerMask == null ? 1 :
                _getCurrentViewPlayerMask();
            return (mask & (1 << (playerNumber - 1))) != 0;
        }

        private static T CreateDelegate<T>(Type apiType, string methodName)
            where T : class
        {
            MethodInfo method = apiType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static
            );
            return method == null ? null :
                Delegate.CreateDelegate(typeof(T), method, false) as T;
        }
    }
}
