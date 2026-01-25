from sqlmodel import select, Session

from src.db.model.user import User

class UserRepository:
    def __init__(self):
        self._model = User

    def get_by_id(self, user_id, session: Session):
        statement = select(self._model).where(self._model.id == user_id)
        result = session.exec(statement)
        return result.first()

    def get_all(self, session: Session):
        statement = select(self._model)
        result = session.exec(statement)
        return result.all()
        