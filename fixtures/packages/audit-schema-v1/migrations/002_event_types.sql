-- Event types emitted by the configured sources.
SET @EVENT_LOGIN := 500101;
SET @EVENT_EXPORT := 500102;

CREATE TABLE IF NOT EXISTS `ae_event_type` (
  `entry` INT UNSIGNED NOT NULL,
  `name` VARCHAR(64) NOT NULL,
  `source_entry` INT UNSIGNED NOT NULL,
  PRIMARY KEY (`entry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

DELETE FROM `ae_event_type` WHERE `entry` IN (@EVENT_LOGIN, @EVENT_EXPORT);

INSERT INTO `ae_event_type` (`entry`, `name`, `source_entry`) VALUES
  (@EVENT_LOGIN, 'User Login', 500001),
  (@EVENT_EXPORT, 'Report Export', 500002);
