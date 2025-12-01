-- The shared authentication database's known realm table (POC.md Cuts:
-- the PoC targets this one schema and writes the row; inspection is
-- production hardening). This file is the operator-supplied schema the
-- tests apply to their scratch authentication databases.

CREATE TABLE IF NOT EXISTS `realmlist` (
  `id` tinyint(3) unsigned NOT NULL auto_increment,
  `name` varchar(32) NOT NULL default '',
  `address` varchar(32) NOT NULL default '127.0.0.1',
  `port` smallint(5) unsigned NOT NULL default '8085',
  `icon` tinyint(3) unsigned NOT NULL default '0',
  `timezone` tinyint(3) unsigned NOT NULL default '0',
  `allowedSecurityLevel` tinyint(3) unsigned NOT NULL default '0',
  `population` float unsigned NOT NULL default '0',
  `realmbuilds` varchar(64) NOT NULL default '',
  PRIMARY KEY  (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Realm System';
