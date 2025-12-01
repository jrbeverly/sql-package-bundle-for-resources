-- Communication preferences added in 1.1.0.
SET @PREFERENCE_EMAIL := 400201;
SET @PREFERENCE_SMS := 400202;
SET @PREFERENCE_POST := 400203;

CREATE TABLE IF NOT EXISTS `cp_profile_preference` (
  `entry` INT UNSIGNED NOT NULL,
  `channel` VARCHAR(64) NOT NULL,
  `priority` TINYINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (`entry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

DELETE FROM `cp_profile_preference` WHERE `entry` IN (@PREFERENCE_EMAIL, @PREFERENCE_SMS, @PREFERENCE_POST);

INSERT INTO `cp_profile_preference` (`entry`, `channel`, `priority`) VALUES
  (@PREFERENCE_EMAIL, 'Email', 1),
  (@PREFERENCE_SMS, 'SMS', 2),
  (@PREFERENCE_POST, 'Post', 3);
