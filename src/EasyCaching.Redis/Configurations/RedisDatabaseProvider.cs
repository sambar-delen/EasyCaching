﻿using Elastic.Apm.StackExchange.Redis;

namespace EasyCaching.Redis
{
    using StackExchange.Redis;
    using StackExchange.Redis.KeyspaceIsolation;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;

    /// <summary>
    /// Redis database provider.
    /// </summary>
    public class RedisDatabaseProvider : IRedisDatabaseProvider
    {
        /// <summary>
        /// The options.
        /// </summary>
        private readonly RedisDBOptions _options;

        private readonly bool _useApm;

        /// <summary>
        /// The connection multiplexer.
        /// </summary>
        private Lazy<ConnectionMultiplexer> _connectionMultiplexer;

        public RedisDatabaseProvider(string name, RedisOptions options)
        {
            _options = options.DBConfig;
            _useApm = options.UseApm;
            _connectionMultiplexer = new Lazy<ConnectionMultiplexer>(CreateConnectionMultiplexer);
            _name = name;
        }

        private readonly string _name;

        public string DBProviderName => this._name;

        /// <summary>
        /// Gets the database connection.
        /// </summary>
        public IDatabase GetDatabase()
        {
            try
            {
                var database = _connectionMultiplexer.Value.GetDatabase();
                
                if (!string.IsNullOrEmpty(_options.KeyPrefix))
                    database = database.WithKeyPrefix(_options.KeyPrefix);
                
                if (_useApm)
                    _connectionMultiplexer.Value.UseElasticApm();

                return database;
            }
            catch (Exception)
            {
                _connectionMultiplexer = new Lazy<ConnectionMultiplexer>(CreateConnectionMultiplexer);
                throw;
            }
        }

        /// <summary>
        /// Gets the server list.
        /// </summary>
        /// <returns>The server list.</returns>
        public IEnumerable<IServer> GetServerList()
        {
            var endpoints = GetMastersServersEndpoints();

            foreach (var endpoint in endpoints)
            {
                yield return _connectionMultiplexer.Value.GetServer(endpoint);
            }
        }

        /// <summary>
        /// Creates the connection multiplexer.
        /// </summary>
        /// <returns>The connection multiplexer.</returns>
        private ConnectionMultiplexer CreateConnectionMultiplexer()
        {
            if (_options.ConfigurationOptions != null)
                return ConnectionMultiplexer.Connect(_options.ConfigurationOptions);

            if (string.IsNullOrWhiteSpace(_options.Configuration))
            {
                var configurationOptions = new ConfigurationOptions
                {
                    ConnectTimeout = _options.ConnectionTimeout,
                    User = _options.Username,
                    Password = _options.Password,
                    Ssl = _options.IsSsl,
                    SslHost = _options.SslHost,
                    AllowAdmin = _options.AllowAdmin,
                    DefaultDatabase = _options.Database,
                    AbortOnConnectFail = _options.AbortOnConnectFail,
                };

                foreach (var endpoint in _options.Endpoints)
                {
                    configurationOptions.EndPoints.Add(endpoint.Host, endpoint.Port);
                }

                return ConnectionMultiplexer.Connect(configurationOptions);
            }
            else
            {
                return ConnectionMultiplexer.Connect(_options.Configuration);
            }
        }

        /// <summary>
        /// Gets the masters servers endpoints.
        /// </summary>
        private List<EndPoint> GetMastersServersEndpoints()
        {
            var masters = new List<EndPoint>();
            foreach (var ep in _connectionMultiplexer.Value.GetEndPoints())
            {
                var server = _connectionMultiplexer.Value.GetServer(ep);
                if (server.IsConnected)
                {
                    //Cluster
                    if (server.ServerType == ServerType.Cluster)
                    {
                        masters.AddRange(server.ClusterConfiguration.Nodes.Where(n => !n.IsReplica).Select(n => n.EndPoint));
                        break;
                    }
                    // Single , Master-Slave
                    if (server.ServerType == ServerType.Standalone && !server.IsReplica)
                    {
                        masters.Add(ep);
                        break;
                    }
                }
            }
            return masters;
        }
    }
}
