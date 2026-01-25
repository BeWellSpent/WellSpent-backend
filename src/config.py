from functools import lru_cache
from pydantic_settings import BaseSettings, SettingsConfigDict
import os



# Determine environment (default to 'dev')
env = os.getenv("ENV", "dev").lower()
env_file = f".env.{env}"

class Settings(BaseSettings):
    app_name: str = "SpendSense"
    debug: bool = False
    database_url: str = ''
    database_connection_args: dict = {"check_same_thread": False}

    model_config = SettingsConfigDict(env_file=env_file, env_file_encoding="utf-8")

@lru_cache()
def get_settings():
    return Settings()
