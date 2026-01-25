from src.db.repository.user_repository import UserRepository
from sqlmodel import Session

class UserService:
    def __init__(self):
        self._repository = UserRepository()

    def get_all(self, session: Session):
        return self._repository.get_all(session)