-- Scoped privileges for integration-test scratch databases (forge_test_<guid>):
-- everything inside them, including creating and dropping them.
GRANT ALL PRIVILEGES ON `forge\_test\_%`.* TO 'forge'@'%';

-- The realm databases a deployment creates (world_<name>, character_<name>).
-- Realm deployment runs as this same operator user; the shared
-- authentication database is provisioned by the operator, not the tool.
GRANT ALL PRIVILEGES ON `world\_%`.* TO 'forge'@'%';
GRANT ALL PRIVILEGES ON `character\_%`.* TO 'forge'@'%';
