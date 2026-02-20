from sqlmodel import select, Session
from db.model.user import User

import logging as log

class UserRepository:
    def __init__(self):
        self._model = User

    def get_by_id(self, user_id, session: Session):
        statement = select(self._model).where(self._model.id == user_id)
        result = session.exec(statement)
        return result.first()
    
    def get_by_email(self, email: str, session: Session):
        statement = select(self._model).where(self._model.email == email)
        result = session.exec(statement)
        return result.first()

    def get_all(self, session: Session):
        statement = select(self._model)
        result = session.exec(statement)
        return result.all()
        
    def create_user(self, session: Session, user_data):
        user = self.get_by_email(user_data.email, session)
        log.info("[UserRepository.create_user] Retrieved user by email %s: %s", user_data.email, user)
        if user and user.email == user_data.email:
            log.error("[UserRepository.create_user] User with email %s already exists", user_data.email)
            raise Exception(f"User with email {user_data.email} already exists.")

        try:
            log.info("[UserRepository.create_user] Creating user with data: %s", user_data)
            user = self._model(
                username=user_data.username,
                email=user_data.email,
                first_name=user_data.first_name,
                last_name=user_data.last_name,
                active=user_data.active
            )
            session.add(user)
            session.commit()
            session.refresh(user)
            return user
        except Exception as e:
            log.error("[UserRepository.create_user] Error creating user: %s", e)
            log.exception(e)
            session.rollback()
            raise e