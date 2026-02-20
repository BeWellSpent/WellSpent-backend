from db.repository.user_repository import UserRepository
from sqlmodel import Session
import logging as log

class UserService:
    def __init__(self):
        self._repository = UserRepository()

    def get_all(self, session: Session):
        return self._repository.get_all(session)
        
    def create_user(self, session: Session, user_data):
        log.info("[UserService.create_user] Creating user with data: %s", user_data)
        return self._repository.create_user(session, user_data)
