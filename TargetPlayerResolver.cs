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

        private delegate PlayerEntity ResolvePlayerDelegate(string user);
        private delegate bool IsPlayerInCurrentViewDelegate(PlayerEntity player);

        private static int _lastResolveAssemblyCount = -1;
        private static ResolvePlayerDelegate _resolvePlayer;
        private static IsPlayerInCurrentViewDelegate _isPlayerInCurrentView;

        public static void ResolveApi()
        {
            if (_resolvePlayer != null && _isPlayerInCurrentView != null)
            {
                return;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (_lastResolveAssemblyCount == assemblies.Length)
            {
                return;
            }

            _lastResolveAssemblyCount = assemblies.Length;
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type apiType = assemblies[i].GetType(ApiTypeName, false);
                if (apiType == null)
                {
                    continue;
                }

                _resolvePlayer = CreateDelegate<ResolvePlayerDelegate>(
                    apiType,
                    "ResolvePlayer"
                );
                _isPlayerInCurrentView =
                    CreateDelegate<IsPlayerInCurrentViewDelegate>(
                        apiType,
                        "IsPlayerInCurrentView"
                    );
                return;
            }
        }

        public static PlayerEntity ResolvePlayer(string user)
        {
            ResolveApi();
            if (_resolvePlayer != null)
            {
                return _resolvePlayer(user);
            }

            return EntityManager.instance == null ? null :
                EntityManager.instance.Find<PlayerEntity>();
        }

        public static bool IsPlayerInCurrentView(PlayerEntity player)
        {
            ResolveApi();
            if (_isPlayerInCurrentView != null)
            {
                return _isPlayerInCurrentView(player);
            }

            PlayerEntity primary = EntityManager.instance == null ? null :
                EntityManager.instance.Find<PlayerEntity>();
            return player != null && ReferenceEquals(player, primary);
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
